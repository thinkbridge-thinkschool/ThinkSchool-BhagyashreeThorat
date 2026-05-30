# Day-11 p11 — Optimizing the slow endpoint (`GET /api/authors/summary`)

**Goal:** reduce p99 latency by ≥ 10× by fixing the intentionally slow endpoint, and
prove it with measurements — *measure → identify → optimize → prove*.

**Environment (unchanged from the Day-11 p10 baseline):**
- DB: Azure SQL `QuotesDb`, **S2 (50 DTU)**, region `southeastasia`.
- Dataset: **200 authors × 250 quotes = 50,000 `Quotes` rows** (re-confirmed live: `SELECT COUNT(*)` → Authors 200, Quotes 50,000).
- App: ASP.NET Core (Release, Production) run **locally** in India → Azure SQL in SE Asia (~30–65 ms round-trip per query).
- Load tool: **bombardier** (`-l --print=result`), single-threaded (`-c 1`) — apples-to-apples with the recorded baseline.
- Only the endpoint code changed between AFTER runs; environment, dataset, DB, and tool were held constant.

---

## A) Before p99

| Before variant | p50 | **p99** | Round-trips | Source |
|---|---|---|---|---|
| **Documented baseline** — N+1 **+ no index** (the original slow state) | 27.90 s | **28.39 s** | 201 | `PerfResults/loadtest-slow-seq.txt` |
| Live re-measure — N+1 **+ covering index present** (isolates the N+1 cost alone) | 13.93 s | **14.39 s** | 201 | `PerfResults/loadtest-slow-withindex-seq.txt` (captured this run) |

The headline "before" is the original slow state: **p99 = 28.39 s**.

## B) After p99

| After | p50 | **p99** | Round-trips | Source |
|---|---|---|---|---|
| **Fixed `/api/authors/summary`** — single set-based query **+ covering index** | 108.69 ms | **183.98 ms** | 1 | `PerfResults/loadtest-fixed-summary-seq.txt` (captured this run) |

## C) Improvement factor

- **Full fix vs original baseline:** 28.39 s → 0.184 s = **≈ 154× faster** on p99 (28 390 ms / 183.98 ms).
- **Code fix alone** (index held constant, N+1 → single query): 14.39 s → 0.184 s = **≈ 78× faster** on p99.

Either way the result clears the **≥ 10×** target by more than an order of magnitude.

---

## D) Code changes made

Single change, in `QuotesApi/Extensions/PerfEndpoints.cs` — the slow `/api/authors/summary` was rewritten in place.

**Before (the N+1):** load all authors, then loop and fire one query *per author*:

```csharp
var authors = await db.Authors.AsNoTracking().OrderBy(a => a.Name).ToListAsync(ct);
foreach (var author in authors)                       // 200 iterations
{
    var quotes = await db.Quotes.AsNoTracking()       // +1 round-trip EACH → 201 total
        .Where(q => q.AuthorId == author.Id && !q.IsDeleted)
        .ToListAsync(ct);
    var latest = quotes.OrderByDescending(q => q.Id).Select(q => q.Text).FirstOrDefault();
    result.Add(new AuthorSummaryDto(author.Id, author.Name, quotes.Count, latest));
}
```

**After (one set-based query):**

```csharp
var result = await db.Authors.AsNoTracking().OrderBy(a => a.Name)
    .Select(a => new AuthorSummaryDto(
        a.Id,
        a.Name,
        a.Quotes.Count(q => !q.IsDeleted),                       // sub-aggregation 1
        a.Quotes.Where(q => !q.IsDeleted)
                .OrderByDescending(q => q.Id)
                .Select(q => q.Text).FirstOrDefault()))          // sub-aggregation 2
    .ToListAsync(ct);
```

**Why this removes the N+1:** the projection has no client-side `foreach` issuing per-row
queries. EF Core translates the two navigation sub-aggregations into a **single SQL
statement** (correlated subqueries / `OUTER APPLY`), so the work is done set-based on the
server in **one round-trip** instead of `1 + N`. Verified by IO: the whole request now
touches `Quotes` in one statement (`Scan count 201, logical reads 1590`) rather than 200
separate statements.

## E) Index changes made

No new index was needed for this run — the justified covering index already exists on the DB and is reused. Its DDL (`Scripts/add-covering-index.sql`):

```sql
CREATE NONCLUSTERED INDEX IX_Quotes_AuthorId
    ON dbo.Quotes (AuthorId)
    INCLUDE (IsDeleted, [Text]);
```

**Justification (from the plans, not assumed):**
- Key column `AuthorId` — the equality predicate (`WHERE AuthorId = @id`) and the join
  key behind every sub-aggregation. Turns a table scan into a seek.
- `INCLUDE (IsDeleted, [Text])` — the only other columns the fixed query reads. Including
  them makes the index **covering**, so the sub-aggregations are satisfied entirely from
  the index with **no key lookup** into the clustered table. The pre-index "after" plan
  for the per-author query (`plan-after.txt`) even emitted a *Missing Index* recommendation
  with exactly these EQUALITY (`IsDeleted`, `AuthorId`) + INCLUDE columns.

## F) Before execution plan summary

Per-author query `WHERE AuthorId = @id AND IsDeleted = 0`, **no index** (`PerfResults/plan-before.txt`):

- Operator: **Clustered Index Scan** of `PK_Quotes` (`PhysicalOp="Clustered Index Scan"`).
- `ActualRowsRead = 50000`, `ActualRows = 250` — reads the entire table to return 250 rows.
- **Logical reads = 1,214** per execution (`Table 'Quotes'. logical reads 1214`).
- This statement runs **once per author (200×)** ⇒ ≈ **242,800 logical reads** and **201
  round-trips** for a single HTTP request. That is the 28.39 s baseline.

## G) After execution plan summary

Fixed single statement, **with covering index** (`PerfResults/plan-after-fixed.txt`, captured this run):

- `Quotes` is now reached via **Index Seek** + an ordered **Index Scan of the nonclustered
  covering `IX_Quotes_AuthorId`** (plus `Top`/`Stream Aggregate`/`Merge Join`/`Nested Loops`
  to assemble count + latest text). The only **Clustered Index Scan** in the plan is on
  `PK_Authors` — just 200 rows / **4 logical reads**.
- **No per-author Clustered Index Scan of the 50k-row `Quotes` table.**
- IO for the **entire request**: `Table 'Quotes'. Scan count 201, logical reads 1590` +
  `Table 'Authors'. logical reads 4`.
- **1 round-trip** total.

**Before → After, on the same single HTTP request:**

| Metric | Before (N+1, no index) | After (1 query, covering index) | Change |
|---|---|---|---|
| SQL round-trips | 201 | 1 | **201× fewer** |
| Logical reads on `Quotes` | ≈ 242,800 (1,214 × 200) | 1,590 | **≈ 150× fewer** |
| `Quotes` access operator | Clustered Index **Scan** (50k rows) | Index **Seek** + covering Index Scan | scan → seek |
| p99 latency | 28.39 s | 183.98 ms | **≈ 154×** |

## H) Why the optimization worked

Two compounding costs were removed:

1. **N+1 round-trips (the dominant cost).** The endpoint made 1 + 200 sequential queries;
   over a ~30–65 ms cross-region link those round-trips alone cost ~14 s (the live
   N+1-with-index re-measure: p99 14.39 s). Collapsing to one set-based statement removes
   200 network round-trips and 200 separate query compilations/executions.

2. **Full-table scans per lookup.** Without an index on `AuthorId`, each child query did a
   Clustered Index Scan of all 50k rows (1,214 logical reads × 200 ≈ 243k reads/request).
   The covering index `IX_Quotes_AuthorId (AuthorId) INCLUDE (IsDeleted, Text)` turns that
   into a seek that reads only covered columns — ~1,590 reads for the whole request.

Together: **201 round-trips → 1**, **~243k logical reads → ~1,590**, and **p99 28.39 s →
0.184 s ≈ 154×**, comfortably exceeding the 10× goal. (Honest caveat unchanged from p10: at
high concurrency on S2 this still recomputes a full per-author aggregation each call;
sustaining 1000 req/s would need a maintained read model — the Day-12 topic. The single-
threaded query-layer numbers above are the apples-to-apples comparison the assignment asks for.)

### Raw evidence files
- Before (baseline, no index): `PerfResults/loadtest-slow-seq.txt`, plan `PerfResults/plan-before.txt`
- Before (N+1 + index, this run): `PerfResults/loadtest-slow-withindex-seq.txt`
- After (fixed endpoint, this run): `PerfResults/loadtest-fixed-summary-seq.txt`, plan `PerfResults/plan-after-fixed.txt`
- Index DDL: `Scripts/add-covering-index.sql`
- Code change: `QuotesApi/Extensions/PerfEndpoints.cs` (`/api/authors/summary`)
