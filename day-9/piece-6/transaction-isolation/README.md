# SQL Server Transaction Isolation Levels — Read Anomalies (Quotes DB)

Demonstrates the three classic read anomalies with **two SQL sessions**, and shows the
isolation level that prevents each one. Run against the project's existing SQL Server
(`docker-compose.yml` → `sqlserver`, database `QuotesDb`, the same instance the API
connection string points at).

No APIs, repositories, services, or schema were redesigned. The only data setup is one
small table, `dbo.Quotes`, created to **mirror the existing EF Core `Quotes` schema
exactly** (`Id` PK identity, `Author`/`Text` `nvarchar(max)`, `IsDeleted` bit,
`OwnerId` int null) and seeded with realistic quotes.

## How to run

```powershell
# 1. Start the existing SQL Server (from the repo root)
docker compose up -d sqlserver

# 2. One-time setup: create QuotesDb + dbo.Quotes + seed data
sqlcmd -S localhost,1433 -U sa -P "YourStrong@Passw0rd" -C -i 00_setup.sql
```

Then either:

* **By hand (two windows)** — open the two `*_sessionA.sql` / `*_sessionB.sql` files in two
  SSMS / Azure Data Studio windows and run them step-by-step in the order each header
  describes (`A1` → `B2` → `A3`). This is the literal "two sessions" demonstration.
* **Automated capture** — `run_demos.ps1` runs both sessions concurrently for every
  scenario, using `WAITFOR DELAY` to interleave them deterministically (B always acts in
  the middle of A's open transaction), resets the data between scenarios, and writes a
  transcript to `_capture_output.txt`. The SQL is identical to the hand-run scripts; only
  the timing waits are added. The **Observed result** blocks below are copied verbatim from
  a real run of that script.

### Seed data (`dbo.Quotes`)

| Id | Author | Text (start) |
|----|--------|--------------|
| 1 | Marcus Aurelius | You have power over your mind... |
| 2 | Marcus Aurelius | The happiness of your life depends... |
| 3 | Seneca | Luck is what happens when preparation meets opportunity. |
| 4 | APJ Abdul Kalam | Dream is not that which you see while sleeping... |
| 5 | Confucius | It does not matter how slowly you go... |

### Note on the engine
These anomalies are a **SQL Server** concept (selectable per-session isolation, lock-based
concurrency). They cannot be reproduced on the project's SQLite dev file, which serializes
the whole database and has no per-session `READ UNCOMMITTED / REPEATABLE READ / SERIALIZABLE`.
`QuotesDb` also has `READ_COMMITTED_SNAPSHOT = OFF` (the default for a new DB), so READ
COMMITTED uses **shared locks** — which is why prevention shows up as *blocking*.

---

## 1. DIRTY READ — reading another transaction's uncommitted change

**A) Isolation level used:** `READ UNCOMMITTED` (Session B)

**B) Session A SQL** — [`01_dirty_read_sessionA.sql`](01_dirty_read_sessionA.sql)
```sql
-- STEP A1: change row 4 but DO NOT commit
BEGIN TRANSACTION;
UPDATE dbo.Quotes
SET    Text = N'!!! UNCOMMITTED EDIT by Session A — will be rolled back !!!'
WHERE  Id = 4;
-- (Session B reads here)
-- STEP A3:
ROLLBACK TRANSACTION;   -- the change never really happened
```

**C) Session B SQL** — [`01_dirty_read_sessionB.sql`](01_dirty_read_sessionB.sql)
```sql
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
-- STEP B2: read row 4 while A's transaction is still open
SELECT Id, Author, Text FROM dbo.Quotes WHERE Id = 4;
```

**D) Observed result**
```
----- SESSION B output -----   (reads while A is uncommitted)
B dirty read (READ UNCOMMITTED) | 4 | !!! UNCOMMITTED EDIT by Session A !!!

----- SESSION A output -----   (after ROLLBACK)
A after ROLLBACK (real value)   | 4 | Dream is not that which you see while sleeping, it...
```
Session B saw `!!! UNCOMMITTED EDIT... !!!`, a value that **never existed** in the committed
database — Session A rolled it back. That is a dirty read of invalid data.

**E) Isolation level that prevents it:** `READ COMMITTED`
```
----- SESSION B output -----   (READ COMMITTED)
B clean read (READ COMMITTED) | 4 | Dream is not that which you see while sleeping, it...
B blocked on lock (ms)        | 2973
```
Under READ COMMITTED, B cannot read the uncommitted row. Its `SELECT` **blocked for ~2973 ms**
until A's transaction ended, then returned the real, committed value.

---

## 2. NON-REPEATABLE READ — the same row changes within one transaction

**A) Isolation level used:** `READ COMMITTED` (Session A)

**B) Session A SQL** — [`02_nonrepeatable_read_sessionA.sql`](02_nonrepeatable_read_sessionA.sql)
```sql
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
BEGIN TRANSACTION;
SELECT Text FROM dbo.Quotes WHERE Id = 3;   -- STEP A1: first read
-- (Session B updates + commits here)
SELECT Text FROM dbo.Quotes WHERE Id = 3;   -- STEP A3: second read
COMMIT TRANSACTION;
```

**C) Session B SQL** — [`02_nonrepeatable_read_sessionB.sql`](02_nonrepeatable_read_sessionB.sql)
```sql
-- STEP B2: a single UPDATE is its own committed transaction
UPDATE dbo.Quotes
SET    Text = N'Sometimes you will never know the value of a moment until it becomes a memory.'
WHERE  Id = 3;
```

**D) Observed result**
```
----- SESSION A output -----
A read #1 (READ COMMITTED) | 3 | Luck is what happens when preparation meets opport...
A read #2 (READ COMMITTED) | 3 | Sometimes you will never know the value of a momen...
----- SESSION B output -----
B committed UPDATE on Id=3 | BlockedMs = 5      <- not blocked; commits immediately
```
Two reads of **the same row in the same transaction** returned **different values** — the read
was not repeatable, because B's committed UPDATE was visible to A's second read.

**E) Isolation level that prevents it:** `REPEATABLE READ`
```
----- SESSION A output -----
A read #1 (REPEATABLE READ) | 3 | Luck is what happens when preparation meets opport...
A read #2 (REPEATABLE READ) | 3 | Luck is what happens when preparation meets opport...
----- SESSION B output -----
B committed UPDATE on Id=3 | BlockedMs = 1987   <- blocked until A committed
```
Under REPEATABLE READ, A's first read holds a shared lock on row 3 until the transaction ends,
so B's UPDATE **blocked ~1987 ms**. Both of A's reads are identical.

---

## 3. PHANTOM READ — a range query gains a new row

**A) Isolation level used:** `REPEATABLE READ` (Session A) — enough to lock existing rows, but
**not** the gaps, so phantoms still get through.

**B) Session A SQL** — [`03_phantom_read_sessionA.sql`](03_phantom_read_sessionA.sql)
```sql
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;
BEGIN TRANSACTION;
SELECT COUNT(*) FROM dbo.Quotes WHERE Author = N'Marcus Aurelius' AND IsDeleted = 0; -- A1
-- (Session B inserts a matching row + commits here)
SELECT COUNT(*) FROM dbo.Quotes WHERE Author = N'Marcus Aurelius' AND IsDeleted = 0; -- A3
COMMIT TRANSACTION;
```

**C) Session B SQL** — [`03_phantom_read_sessionB.sql`](03_phantom_read_sessionB.sql)
```sql
-- STEP B2: new quote by the SAME author -> falls inside A's range predicate
INSERT INTO dbo.Quotes (Author, Text, IsDeleted, OwnerId)
VALUES (N'Marcus Aurelius',
        N'Waste no more time arguing about what a good man should be. Be one.', 0, NULL);
```

**D) Observed result**
```
----- SESSION A output -----
A range COUNT #1 (REPEATABLE READ) | 2
A range COUNT #2 (REPEATABLE READ) | 3      <- phantom row appeared
----- SESSION B output -----
B inserted matching row | BlockedMs = 5     <- not blocked; commits immediately
```
The same range query returned **2 rows, then 3** within one transaction. The extra row is a
phantom — REPEATABLE READ does not lock the range, so B's INSERT slipped in.

**E) Isolation level that prevents it:** `SERIALIZABLE`
```
----- SESSION A output -----
A range COUNT #1 (SERIALIZABLE) | 2
A range COUNT #2 (SERIALIZABLE) | 2          <- no phantom
----- SESSION B output -----
B inserted matching row | BlockedMs = 2000   <- blocked by key-range lock until A committed
```
Under SERIALIZABLE, A's range query takes a **key-range lock** over the whole predicate, so
B's INSERT into that range **blocked ~2000 ms** until A committed. The count is stable.

---

## Summary

| Anomaly | Happens at / below | Prevented by |
|--------------------|----------------------------|--------------------|
| Dirty read         | READ UNCOMMITTED           | **READ COMMITTED** |
| Non-repeatable read| READ UNCOMMITTED, READ COMMITTED | **REPEATABLE READ** |
| Phantom read       | up to and incl. REPEATABLE READ | **SERIALIZABLE** |

Each level prevents its own anomaly **and** every weaker one. The trade-off is concurrency:
stronger isolation means more/longer locking (visible above as the `BlockedMs` waits), so use
the weakest level that is correct for the workload.

## Files
| File | Purpose |
|------|---------|
| `00_setup.sql` | Create `QuotesDb` + `dbo.Quotes` (mirrors existing schema) + seed |
| `01_dirty_read_sessionA.sql` / `_sessionB.sql` | Dirty read, two windows |
| `02_nonrepeatable_read_sessionA.sql` / `_sessionB.sql` | Non-repeatable read, two windows |
| `03_phantom_read_sessionA.sql` / `_sessionB.sql` | Phantom read, two windows |
| `run_demos.ps1` | Runs all six scenarios concurrently and captures real output |
| `_capture_output.txt` | Transcript from the last automated run |
