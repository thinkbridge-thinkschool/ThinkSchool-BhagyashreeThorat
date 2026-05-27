/* =====================================================================
   NON-REPEATABLE READ  —  SESSION A   (reads the SAME row twice)

   Run this in WINDOW 1.  Coordinate by step number:

        A1  (this window)   -> first read of quote #3
        B2  (other window)  -> Session B UPDATEs quote #3 and COMMITs
        A3  (this window)   -> second read of quote #3 (inside the SAME tran)

   Two variants below:
     (1) ANOMALY    — READ COMMITTED  -> second read DIFFERS from the first
     (2) PREVENTION — REPEATABLE READ -> second read MATCHES the first
                                         (B's UPDATE is blocked until A commits)
   Run ONE block per pass.  Re-run 00_setup.sql between passes.
   ===================================================================== */
USE QuotesDb;
GO

-- =====================================================================
-- (1) ANOMALY:  READ COMMITTED allows a non-repeatable read
-- =====================================================================
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
BEGIN TRANSACTION;

-------------------------------------------------------------------- STEP A1
SELECT 'A first read (READ COMMITTED)' AS Stage, Id, Author, Text
FROM   dbo.Quotes WHERE Id = 3;
-- >>> Switch to Session B, run STEP B2 (UPDATE + COMMIT). Then come back. <<<
GO

-------------------------------------------------------------------- STEP A3
-- Same query, same transaction — but the value has CHANGED. Not repeatable.
SELECT 'A second read (READ COMMITTED)' AS Stage, Id, Author, Text
FROM   dbo.Quotes WHERE Id = 3;

COMMIT TRANSACTION;
GO


-- =====================================================================
-- (2) PREVENTION:  REPEATABLE READ keeps the row stable
-- =====================================================================
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;
BEGIN TRANSACTION;

-------------------------------------------------------------------- STEP A1
-- This read takes a shared lock that is HELD until the transaction ends.
SELECT 'A first read (REPEATABLE READ)' AS Stage, Id, Author, Text
FROM   dbo.Quotes WHERE Id = 3;
-- >>> Switch to Session B, run STEP B2. B's UPDATE will BLOCK (held lock). <<<
GO

-------------------------------------------------------------------- STEP A3
-- Identical to the first read — the value could not change underneath us.
SELECT 'A second read (REPEATABLE READ)' AS Stage, Id, Author, Text
FROM   dbo.Quotes WHERE Id = 3;

COMMIT TRANSACTION;   -- only now is Session B's blocked UPDATE allowed to proceed
GO
