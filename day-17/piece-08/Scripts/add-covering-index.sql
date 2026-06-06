-- Day-11 perf lab — the "after" fix for the missing index.
-- Run this AFTER you have captured the slow baseline (plan + p50/p99).
--
-- Key column AuthorId makes the per-author lookup a seek instead of a scan.
-- INCLUDE (IsDeleted, Text) makes it a COVERING index: every column the query
-- reads lives in the index, so SQL Server never touches the clustered index
-- (no key lookups). This ties directly back to Day-8 covering indexes.

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_Quotes_AuthorId' AND object_id = OBJECT_ID('dbo.Quotes'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Quotes_AuthorId
        ON dbo.Quotes (AuthorId)
        INCLUDE (IsDeleted, [Text]);
    PRINT 'Created IX_Quotes_AuthorId (covering).';
END
ELSE
    PRINT 'IX_Quotes_AuthorId already exists.';
