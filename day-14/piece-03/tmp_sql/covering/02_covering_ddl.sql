-- Day 8 Piece 5: Covering Index with INCLUDE — DDL
-- Convert IX_QuotePerformanceTest_Author into a covering index for the query.
-- Adding [Text] to INCLUDE means every column the query touches is already in
-- the non-clustered leaf, so the engine no longer needs to walk back to the
-- clustered index — the Key Lookup disappears.
--
-- DROP_EXISTING = ON rebuilds the same-name index in a single step, preserving
-- the original intent (key=Author, filter=IsDeleted=0) and only widening the
-- INCLUDE list with the column the existing query actually needs.
USE QuotesDb;
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

CREATE NONCLUSTERED INDEX IX_QuotePerformanceTest_Author
    ON dbo.QuotePerformanceTest (Author)
    INCLUDE (Category, CreatedDate, [Text])
    WHERE IsDeleted = 0
    WITH (DROP_EXISTING = ON);
GO

-- Confirm the new shape.
SELECT i.name AS IndexName, c.name AS ColumnName,
       ic.key_ordinal, ic.is_included_column
FROM sys.indexes i
JOIN sys.index_columns ic
  ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.columns c
  ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE i.object_id = OBJECT_ID('dbo.QuotePerformanceTest')
  AND i.name = 'IX_QuotePerformanceTest_Author'
ORDER BY ic.is_included_column, ic.key_ordinal, c.name;
GO
