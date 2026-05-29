# Day 8 — Piece 5: Covering Index with INCLUDE (Key Lookup Eliminated)

Database: existing **QuotesDb** (SQL Server 2022 in Docker).
Table: **`dbo.QuotePerformanceTest`** (100,000 rows) — the same table from
[piece 4](../indexing/RESULTS.md). No new schema, no new data, no application
changes.

Scripts: [01_before.sql](01_before.sql), [02_covering_ddl.sql](02_covering_ddl.sql),
[03_after.sql](03_after.sql). Raw run output: [before.out](before.out),
[after.out](after.out).

All measurements taken with `SET STATISTICS IO ON` + `SET STATISTICS TIME ON`,
data cache flushed with `DBCC DROPCLEANBUFFERS` and plan cache with
`DBCC FREEPROCCACHE` before each run. Execution plan captured with
`SET SHOWPLAN_TEXT ON` (the headless-sqlcmd stand-in for SSMS Actual Execution
Plan).

---

## 1. Starting point — existing indexes

From [piece 4](../indexing/03_indexes.sql), `IX_QuotePerformanceTest_Author`
already existed as:

```sql
CREATE NONCLUSTERED INDEX IX_QuotePerformanceTest_Author
    ON dbo.QuotePerformanceTest (Author)
    INCLUDE (Category, CreatedDate)
    WHERE IsDeleted = 0;
```

That index covers the original `SELECT Id, Author, Category, CreatedDate FROM …
WHERE Author = ? AND IsDeleted = 0` query. The instant the application also
needs the `[Text]` column, the index is no longer covering — every matched row
has to be fetched from the clustered index.

---

## 2. The query (chosen to trigger a Key Lookup)

```sql
SELECT Id, Author, [Text], Category, CreatedDate
FROM dbo.QuotePerformanceTest WITH (INDEX (IX_QuotePerformanceTest_Author))
WHERE Author = N'Marcus Aurelius'
  AND IsDeleted = 0;
-- 2,000 rows match (50 % of Marcus Aurelius rows; the rest are IsDeleted = 1).
```

The `WITH (INDEX (...))` hint isolates the Key Lookup cost. Without the hint
the optimizer picks a clustered index scan (~4,367 reads) because doing 2,000
per-row lookups looks more expensive than scanning the table — exactly the
"tipping point" lesson from piece 4. The hint pins the index so we can see the
lookup cost directly, and so the AFTER plan is an apples-to-apples comparison
on the same index.

---

## A) BEFORE plan summary

```
|--Nested Loops(Inner Join, OUTER REFERENCES:([Id], [Expr1002]) WITH UNORDERED PREFETCH)
     |--Index Seek (NonClustered) IX_QuotePerformanceTest_Author
     |       SEEK: Author = N'Marcus Aurelius'
     |--Clustered Index Seek (LOOKUP) CIX_QuotePerformanceTest_Id
             SEEK: Id = Id
```

- **Key Lookup operator**: the inner `Clustered Index Seek (… LOOKUP ORDERED FORWARD)`
  driven by `Nested Loops` is the Key Lookup. (In SSMS graphical plans this
  appears as a discrete "Key Lookup (Clustered)" icon — same physical operator,
  rendered with a friendlier label.)
- Each of the ~2,000 matched rows triggers one clustered-index seek to fetch
  `[Text]`, which is the column missing from the non-clustered leaf.
- See raw [before.out](before.out) line 10 and lines 28–31 for the literal
  STATISTICS IO and SHOWPLAN_TEXT output.

---

## B) Index DDL with INCLUDE

```sql
CREATE NONCLUSTERED INDEX IX_QuotePerformanceTest_Author
    ON dbo.QuotePerformanceTest (Author)
    INCLUDE (Category, CreatedDate, [Text])   -- +Text turns it into a covering index
    WHERE IsDeleted = 0
    WITH (DROP_EXISTING = ON);
```

Design choices:

- **`[Text]` added to INCLUDE** — the one column that was forcing the Key
  Lookup. Adding it kills the lookup; not adding it would have meant SQL still
  had to walk back to the clustered index.
- **Only `[Text]`** — the index already had `Category` and `CreatedDate` for
  the other query shapes; `Id` is implicitly present because it is the
  clustered key. Nothing else was added so the leaf stays as narrow as the
  query genuinely requires.
- **`DROP_EXISTING = ON`** — same name, same key, same filter, just a wider
  INCLUDE list. Keeps the design intent of the original piece-4 index and
  avoids polluting the table with a parallel "_v2" index.
- **Filter preserved** (`WHERE IsDeleted = 0`) — the application still only
  reads non-deleted quotes, so the filtered index stays smaller than a full
  non-clustered would.

---

## C) AFTER plan summary

```
|--Index Seek (NonClustered) IX_QuotePerformanceTest_Author
       SEEK: Author = N'Marcus Aurelius'
       ORDERED FORWARD
```

- **No Nested Loops, no Clustered Index Seek (LOOKUP) → Key Lookup is gone.**
- The query is now served entirely from the non-clustered index leaf. Every
  column the query asks for is either the index key (`Author`), included
  (`Category`, `CreatedDate`, `[Text]`), or the implicit row locator (`Id`,
  which is the clustered key carried in every NC leaf row).
- See raw [after.out](after.out) line 10 and line 29.

---

## D) Logical reads comparison

```
Before:
Logical reads: 10,568

After:
Logical reads: 95

Delta: -10,473 reads  (≈ 111× fewer; AFTER is ~0.9 % of BEFORE)
```

| | BEFORE (Key Lookup) | AFTER (covering INCLUDE) |
|---|---:|---:|
| Logical reads | **10,568** | **95** |
| Plan shape | Nested Loops → NC Seek + Clustered Seek (LOOKUP) | NC Seek only |
| CPU | 49 ms | 1 ms |
| Elapsed | 126 ms | 5 ms |

The drop comes from removing ~2,000 per-row clustered-index seeks. Adding
`[Text]` to the INCLUDE list widens the index leaf modestly (one extra
NVARCHAR(1000) per row, still well under one row per leaf page) — a one-time
storage cost that buys two orders of magnitude fewer reads on every execution
of this query shape.
