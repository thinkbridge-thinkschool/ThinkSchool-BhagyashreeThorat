# Day 8 — Piece 4: Clustered vs Non-Clustered Indexes

Database: existing **QuotesDb** (SQL Server 2022 in Docker, per `docker-compose.yml`).
Experiment table: **`dbo.QuotePerformanceTest`** — created *only* for these tests, production tables untouched.
Row count: **100,000**.

All measurements taken with `SET STATISTICS IO ON` + `SET STATISTICS TIME ON`, and the data cache flushed via `DBCC DROPCLEANBUFFERS` / `DBCC FREEPROCCACHE` before each run so logical reads reflect a cold-cache, fresh-plan scenario. Execution plans were verified with `SET SHOWPLAN_TEXT ON` (stand-in for Actual Execution Plan in SSMS).

Scripts: [01_setup.sql](01_setup.sql), [02_measure.sql](02_measure.sql), [03_indexes.sql](03_indexes.sql), [04_plans.sql](04_plans.sql), [05_write_cost.sql](05_write_cost.sql).

---

## A) Index DDL

```sql
-- (A) CLUSTERED on Id — physical row order = surrogate key order.
--     Single-row API lookups (GET /quotes/{id}) hit one leaf page directly.
CREATE UNIQUE CLUSTERED INDEX CIX_QuotePerformanceTest_Id
    ON dbo.QuotePerformanceTest (Id);

-- (B) NON-CLUSTERED #1: Author, filtered to non-deleted rows.
--     "List quotes by author" is the most frequent list query, and the API
--     already filters IsDeleted = 0, so a filtered index stays smaller.
CREATE NONCLUSTERED INDEX IX_QuotePerformanceTest_Author
    ON dbo.QuotePerformanceTest (Author)
    INCLUDE (Category, CreatedDate)
    WHERE IsDeleted = 0;

-- (C) NON-CLUSTERED #2: (Category, CreatedDate DESC), covering Author + Text.
--     "Latest N quotes in this category" feed query. Category leads the seek,
--     CreatedDate DESC removes the Sort, INCLUDE columns make the index covering
--     so no key lookup back to the clustered index is needed.
CREATE NONCLUSTERED INDEX IX_QuotePerformanceTest_Category_CreatedDate
    ON dbo.QuotePerformanceTest (Category, CreatedDate DESC)
    INCLUDE (Author, [Text]);
```

---

## B) Queries (one per index) + C) Logical reads BEFORE / AFTER

### Q1 — Clustered index (lookup by `Id`)

```sql
SELECT Id, Author, Category, CreatedDate
FROM dbo.QuotePerformanceTest
WHERE Id = 75123;
```

| | Before (heap) | After (clustered) |
|---|---|---|
| Logical reads | **4,367** | **3** |
| Plan | Table Scan | `Clustered Index Seek (CIX_QuotePerformanceTest_Id)` |
| CPU | 11 ms | 0 ms |

**~1,455× fewer pages read.** The clustered B-tree walks root → intermediate → leaf (3 pages) directly to the row.

---

### Q2 — Non-clustered index #1 (filter by `Author`)

```sql
SELECT Id, Author, Category, CreatedDate
FROM dbo.QuotePerformanceTest
WHERE Author = N'Marcus Aurelius'
  AND IsDeleted = 0;
-- 2,000 rows matched
```

| | Before (heap) | After (filtered NC) |
|---|---|---|
| Logical reads | **4,367** | **23** |
| Plan | Table Scan | `Index Seek (IX_QuotePerformanceTest_Author)` |
| CPU | 16 ms | 1 ms |

**~190× fewer pages.** The filter `WHERE IsDeleted = 0` matches the index's `WHERE` clause, so the optimizer used it without a residual predicate. All four SELECT columns are either the key (`Author`) or `INCLUDE`d (`Category`, `CreatedDate`) or the row locator (`Id`, which is the clustered key), so no key lookup.

---

### Q3 — Non-clustered index #2 (`Category` + latest `CreatedDate`)

```sql
SELECT TOP 50 Id, Author, Category, CreatedDate
FROM dbo.QuotePerformanceTest
WHERE Category = N'Wisdom'
  AND CreatedDate >= '2025-01-01'
ORDER BY CreatedDate DESC;
```

| | Before (heap) | After (composite NC) |
|---|---|---|
| Logical reads | **4,367** | **9** |
| Plan | Table Scan + Sort + Top | `Index Seek (IX_QuotePerformanceTest_Category_CreatedDate)` + Top |
| CPU | 18 ms | 0 ms |

**~485× fewer pages.** Because `CreatedDate DESC` is encoded in the index key order, the seek streams rows already sorted; the `Top 50` short-circuits after the first 50 leaf entries, so the index is barely touched.

---

## D) Write-side cost (one line)

> **Insert of 5,000 rows became ~3× slower on the indexed table (74 ms vs. 25 ms) and required an extra ~11,700 Worktable logical reads to maintain the two non-clustered B-trees and the filtered index predicate — the classic read-fast / write-slower trade-off.**

Raw numbers from [05_write_cost.sql](05_write_cost.sql):

| Target | Logical reads (target) | Worktable reads | CPU | Elapsed |
|---|---:|---:|---:|---:|
| Heap, no indexes | 5,238 | 0 | 21 ms | 25 ms |
| 1 clustered + 2 non-clustered | 2,931 | 11,747 | 67 ms | 74 ms |

(The heap's own logical reads are higher because each row goes through allocation/PFS/IAM pages; the indexed table's extra cost shows up as the Worktable used for batch index maintenance and as elapsed time.)

---

## Summary

| Query | BEFORE reads | AFTER reads | Speedup |
|---|---:|---:|---:|
| Q1 (clustered seek by Id) | 4,367 | **3** | ~1,455× |
| Q2 (NC seek on Author) | 4,367 | **23** | ~190× |
| Q3 (NC seek on Category + CreatedDate DESC) | 4,367 | **9** | ~485× |
