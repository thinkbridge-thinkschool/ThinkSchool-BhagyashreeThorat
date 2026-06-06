-- Rollback to the "before" (missing index) state so you can re-capture the
-- slow baseline or demo the difference again.

IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_Quotes_AuthorId' AND object_id = OBJECT_ID('dbo.Quotes'))
BEGIN
    DROP INDEX IX_Quotes_AuthorId ON dbo.Quotes;
    PRINT 'Dropped IX_Quotes_AuthorId.';
END
ELSE
    PRINT 'IX_Quotes_AuthorId does not exist.';
