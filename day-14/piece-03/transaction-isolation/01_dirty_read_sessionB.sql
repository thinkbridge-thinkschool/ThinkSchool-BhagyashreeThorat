/* =====================================================================
   DIRTY READ  —  SESSION B   (the reader)

   Run this in WINDOW 2, at STEP B2 (i.e. after Session A's STEP A1,
   while A's transaction is still open and uncommitted).

   Two variants below:
     (1) ANOMALY    — READ UNCOMMITTED  -> sees A's dirty, uncommitted value
     (2) PREVENTION — READ COMMITTED    -> blocks until A finishes, then
                                           reads the real (rolled-back) value
   Run ONE block per pass.  Re-run 00_setup.sql between passes for a clean state.
   ===================================================================== */
USE QuotesDb;
GO

-- =====================================================================
-- (1) ANOMALY:  READ UNCOMMITTED allows the dirty read
-- =====================================================================
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;   -- a.k.a. WITH (NOLOCK)

-------------------------------------------------------------------- STEP B2
-- Returns A's UNCOMMITTED text. This value is invalid — A will roll it back.
SELECT 'B dirty read (READ UNCOMMITTED)' AS Stage, Id, Author, Text
FROM   dbo.Quotes WHERE Id = 4;
GO
-- >>> Now go back to Session A and run STEP A3 (ROLLBACK).               <<<
-- >>> The text B just printed never existed in the committed database.  <<<


-- =====================================================================
-- (2) PREVENTION:  READ COMMITTED forbids the dirty read
--     (SQL Server's default; RCSI is OFF on this DB, so it uses locks)
-- =====================================================================
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

-------------------------------------------------------------------- STEP B2
-- This query BLOCKS while A's transaction is open (it waits for the lock).
-- When A runs STEP A3 (ROLLBACK), this unblocks and returns the REAL value.
-- Result: B never sees uncommitted data.
SELECT 'B clean read (READ COMMITTED)' AS Stage, Id, Author, Text
FROM   dbo.Quotes WHERE Id = 4;
GO
