# Day-11 — Profiling a slow endpoint (authors → quotes)

**Target DB:** Azure SQL Database `QuotesDb`, service tier **S2 (50 DTU)**, region `southeastasia`
**Load tool:** bombardier v1.2.6 — `-l --print=result`
**Dataset:** 200 authors × 250 quotes = **50,000** `Quotes` rows (via `POST /api/authors/seed`)
**Client:** local dev machine in India → Azure SQL in SE Asia (≈30–65 ms round-trip per query)

Same result, two implementations:

- `GET /api/authors/summary` — **slow**: 1 + N queries (N+1); each child query scans
  `Quotes` because the `AuthorId` FK is unindexed.
- `GET /api/authors/summary-fast` — **fast**: one set-based statement, served by a
  covering index.

---

## 1. The offending SQL

**Slow (N+1): 1 + 200 = 201 round-trips per request.**

```sql
-- Query #1 — load every author
SELECT [a].[Id], [a].[Name]
FROM [Authors] AS [a]
ORDER BY [a].[Name];

-- Queries #2 … #201 — fired once PER author inside a foreach loop (the N+1)
SELECT [q].[Id], [q].[Author], [q].[AuthorId], [q].[IsDeleted], [q].[OwnerId], [q].[Text]
FROM [Quotes] AS [q]
WHERE [q].[AuthorId] = @author_Id AND [q].[IsDeleted] = CAST(0 AS bit);
```

**Fast: one statement** (EF compiles the two navigation subqueries into correlated
subqueries / OUTER APPLY):

```sql
SELECT [a].[Id], [a].[Name],
    (SELECT COUNT(*) FROM [Quotes] AS [q]
       WHERE [a].[Id] = [q].[AuthorId] AND [q].[IsDeleted] = CAST(0 AS bit)),
    (SELECT TOP(1) [q0].[Text] FROM [Quotes] AS [q0]
       WHERE [a].[Id] = [q0].[AuthorId] AND [q0].[IsDeleted] = CAST(0 AS bit)
       ORDER BY [q0].[Id] DESC)
FROM [Authors] AS [a]
ORDER BY [a].[Name];
```

---

## 2. Execution plan (captured on Azure SQL via `SET STATISTICS XML/IO ON`)

For one per-author lookup `WHERE AuthorId = 1 AND IsDeleted = 0`:

| State | Operator on `Quotes` | Logical reads |
|------|------|------|
| **Before** (no index) | **Clustered Index Scan** | **1,214** |
| **After** (`IX_Quotes_AuthorId`) | **Index Seek** (+ key lookup) | 782 |

- Before: every one of the 200 child queries scans all 50k rows → ~243,000 logical
  reads for a single HTTP request.
- After: the covering index `IX_Quotes_AuthorId (AuthorId) INCLUDE (IsDeleted, Text)`
  turns the scan into a seek. The *fast* query reads only covered columns, so it is a
  pure covering seek (no lookup). The *slow* query still key-looks-up `Author`/`OwnerId`.

Raw captures: `PerfResults/plan-before.txt`, `PerfResults/plan-after.txt`.

---

## 3. Latency under load (bombardier, p50 / p99)

| Scenario | Conc. | Reqs | p50 | p99 | Errors |
|------|------|------|------|------|------|
| **BASELINE** slow, N+1 + no index | 1 | 6 | **27.90 s** | **28.39 s** | 0 |
| Slow, N+1 + no index | 4 | 16 | 10 min (timeout) | 10 min (timeout) | **15/16 timed out** |
| **AFTER** fast, 1 query + covering index | 1 | 100 | **109 ms** | **170 ms** | 0 |
| Fast, 1 query + covering index | 50 | 149 | 7.07 s | 31.0 s | 0 |

**Headline (apples-to-apples, single-threaded): p50 27.90 s → 109 ms ≈ 255× faster; p99 28.39 s → 170 ms.**

Raw captures: `PerfResults/loadtest-*.txt`.

---

## 4. What fixed it

1. **Collapsed the N+1** — replaced the per-author loop with a single projection off
   `Authors`. 201 round-trips → 1.
2. **Added the missing index** — `IX_Quotes_AuthorId (AuthorId) INCLUDE (IsDeleted, Text)`
   (`Scripts/add-covering-index.sql`): per-author scan → covering seek.

Each fix matters independently:
- N+1 alone (fast query, **no** index): single request ≈ **13 s** (one statement, but
  200 APPLY *scans*).
- Index alone (slow N+1 endpoint, **with** index): ≈ **13 s** (seeks, but still 201
  round-trips).
- **Both together: ≈ 0.09 s.**

---

## 5. Honest caveat → leads into Day 12

Even fixed, the fast endpoint at **c=50 on S2** degrades to p50 ≈ 7 s: it still
recomputes a full per-author aggregation over 50k rows on every call, and S2's 50 DTU
saturates. Hitting the week target (1000 req/s, p99 < 100 ms) for this shape needs a
**maintained read model / cached projection** — exactly the Day-12 (CQRS-lite) topic.
At low concurrency the query layer itself is already well under 100 ms (p50 109 ms here,
dominated by ~30–65 ms cross-region network round-trips; from a co-located app it would
be lower).

---

## Exercise answers

**Baseline p50/p99:** `/api/authors/summary` (N+1 + missing index), single-threaded on
Azure SQL S2 over 50k rows: **p50 = 27.90 s, p99 = 28.39 s**. Under just 4 concurrent
users it collapses entirely (15/16 requests time out).

**Offending SQL:** the N+1 — one `SELECT … FROM [Authors]` followed by 200 ×
`SELECT … FROM [Quotes] WHERE [AuthorId] = @author_Id AND [IsDeleted] = 0` (§1).

**Plan:** each per-author query is a **Clustered Index Scan** of all 50k `Quotes`
rows (1,214 logical reads each; ~243k per request). After the covering index it is an
**Index Seek** (§2).

**The two biggest problems:**
1. **N+1 round-trips.** 201 sequential queries per request; network/latency-bound. The
   single dominant fix — collapse to one set-based query.
2. **Missing index on `Quotes.AuthorId`.** EF auto-creates an FK index; it was stripped
   in the migration to reproduce the anti-pattern, forcing a full table scan on every
   lookup. Fixed with a nonclustered **covering** index (`INCLUDE (IsDeleted, Text)`).
