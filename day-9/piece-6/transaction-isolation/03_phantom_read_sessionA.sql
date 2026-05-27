/* =====================================================================
   PHANTOM READ  —  SESSION A   (queries the SAME range twice)

   Range query: "all quotes by Marcus Aurelius".  Run in WINDOW 1:

        A1  (this window)   -> count rows in the range (starts at 2)
        B2  (other window)  -> Session B INSERTs another matching row + COMMITs
        A3  (this window)   -> re-run the SAME range query

   Two variants below:
     (1) ANOMALY    — REPEATABLE READ -> a NEW (phantom) row appears in A3.
                      REPEATABLE READ locks the rows it already read, but NOT
                      the gaps, so an INSERT into the range still slips in.
     (2) PREVENTION — SERIALIZABLE    -> a key-range lock covers the whole
                      predicate, so B's INSERT is BLOCKED; A3 == A1.
   Run ONE block per pass.  Re-run 00_setup.sql between passes.
   ===================================================================== */
USE QuotesDb;
GO

-- =====================================================================
-- (1) ANOMALY:  REPEATABLE READ still allows phantoms
-- =====================================================================
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;
BEGIN TRANSACTION;

-------------------------------------------------------------------- STEP A1
SELECT 'A first range read (REPEATABLE READ)' AS Stage, Id, Text
FROM   dbo.Quotes WHERE Author = N'Marcus Aurelius' AND IsDeleted = 0;
SELECT 'A first range COUNT' AS Stage, COUNT(*) AS Rows
FROM   dbo.Quotes WHERE Author = N'Marcus Aurelius' AND IsDeleted = 0;
-- >>> Switch to Session B, run STEP B2 (INSERT + COMMIT). Then come back. <<<
GO

-------------------------------------------------------------------- STEP A3
-- Same predicate, same transaction — but an extra row has appeared. Phantom.
SELECT 'A second range read (REPEATABLE READ)' AS Stage, Id, Text
FROM   dbo.Quotes WHERE Author = N'Marcus Aurelius' AND IsDeleted = 0;
SELECT 'A second range COUNT' AS Stage, COUNT(*) AS Rows
FROM   dbo.Quotes WHERE Author = N'Marcus Aurelius' AND IsDeleted = 0;

COMMIT TRANSACTION;
GO


-- =====================================================================
-- (2) PREVENTION:  SERIALIZABLE locks the range
-- =====================================================================
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;

-------------------------------------------------------------------- STEP A1
-- This range read takes a key-range lock over the predicate.
SELECT 'A first range COUNT (SERIALIZABLE)' AS Stage, COUNT(*) AS Rows
FROM   dbo.Quotes WHERE Author = N'Marcus Aurelius' AND IsDeleted = 0;
-- >>> Switch to Session B, run STEP B2. B's INSERT will BLOCK (range lock). <<<
GO

-------------------------------------------------------------------- STEP A3
-- Same count as before — no phantom could be inserted into the locked range.
SELECT 'A second range COUNT (SERIALIZABLE)' AS Stage, COUNT(*) AS Rows
FROM   dbo.Quotes WHERE Author = N'Marcus Aurelius' AND IsDeleted = 0;

COMMIT TRANSACTION;   -- only now is Session B's blocked INSERT allowed to proceed
GO
