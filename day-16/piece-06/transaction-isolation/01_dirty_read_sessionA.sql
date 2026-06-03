/* =====================================================================
   DIRTY READ  —  SESSION A   (the writer that never commits)

   Run this in WINDOW 1.  Coordinate with Session B by step number:

        A1  (this window)   -> begin tran + UPDATE, do NOT commit
        B2  (other window)  -> Session B reads the uncommitted value
        A3  (this window)   -> ROLLBACK  (the value never really existed)

   Applies to BOTH the anomaly run and the prevention run — Session A is
   identical; only Session B's isolation level changes.
   ===================================================================== */
USE QuotesDb;
GO

-------------------------------------------------------------------- STEP A1
-- Start a transaction and change quote #4, but leave it OPEN (no commit).
BEGIN TRANSACTION;

UPDATE dbo.Quotes
SET    Text = N'!!! UNCOMMITTED EDIT by Session A — will be rolled back !!!'
WHERE  Id = 4;

SELECT 'A after UPDATE (uncommitted)' AS Stage, Id, Author, Text
FROM   dbo.Quotes WHERE Id = 4;
-- >>> Now switch to Session B and run STEP B2. <<<
GO

-------------------------------------------------------------------- STEP A3
-- Throw the change away. It was never committed, so the real value is restored.
ROLLBACK TRANSACTION;

SELECT 'A after ROLLBACK (real value)' AS Stage, Id, Author, Text
FROM   dbo.Quotes WHERE Id = 4;
GO
