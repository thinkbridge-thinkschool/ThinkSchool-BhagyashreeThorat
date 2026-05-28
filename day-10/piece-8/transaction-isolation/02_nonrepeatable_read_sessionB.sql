/* =====================================================================
   NON-REPEATABLE READ  —  SESSION B   (the writer)

   Run this in WINDOW 2 at STEP B2, i.e. AFTER Session A's first read (A1)
   and BEFORE Session A's second read (A3).

   This same Session B is used for both passes:
     - vs A in READ COMMITTED   -> this UPDATE commits immediately
     - vs A in REPEATABLE READ  -> this UPDATE BLOCKS until A commits
   ===================================================================== */
USE QuotesDb;
GO

-------------------------------------------------------------------- STEP B2
-- A single UPDATE is its own committed transaction (autocommit).
UPDATE dbo.Quotes
SET    Text = N'Sometimes you will never know the value of a moment until it becomes a memory.'
WHERE  Id = 3;

SELECT 'B committed UPDATE' AS Stage, Id, Author, Text
FROM   dbo.Quotes WHERE Id = 3;
GO
-- >>> Now go back to Session A and run STEP A3 (its second read). <<<
