-- Index DDL for the experiment.
USE QuotesDb;
GO

-- Filtered indexes require these SET options at create time.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

-- (A) CLUSTERED INDEX on Id
--     Why: Id is the natural unique row identifier and the surrogate key the
--          application would use for single-row lookups (GET /quotes/{id}).
--          Clustered = the table is physically sorted by Id, so seeks return
--          the full row from the leaf without a key lookup.
CREATE UNIQUE CLUSTERED INDEX CIX_QuotePerformanceTest_Id
    ON dbo.QuotePerformanceTest (Id);
GO

-- (B) NON-CLUSTERED INDEX #1 on Author (filtered to non-deleted rows)
--     Why: "find all quotes by author" is the most common list query and the
--          existing API already filters out deleted quotes. A filtered index
--          stays small (~98% of rows) and matches the actual WHERE shape.
CREATE NONCLUSTERED INDEX IX_QuotePerformanceTest_Author
    ON dbo.QuotePerformanceTest (Author)
    INCLUDE (Category, CreatedDate)
    WHERE IsDeleted = 0;
GO

-- (C) NON-CLUSTERED INDEX #2 on (Category, CreatedDate DESC) covering Author/Text
--     Why: "show me the latest N quotes in this category" is a typical
--          browse / feed query. Putting Category first allows the seek,
--          CreatedDate DESC lets the engine walk the leaf in order
--          (no Sort operator), and INCLUDE columns make it covering so no
--          key lookups are needed for the displayed columns.
CREATE NONCLUSTERED INDEX IX_QuotePerformanceTest_Category_CreatedDate
    ON dbo.QuotePerformanceTest (Category, CreatedDate DESC)
    INCLUDE (Author, [Text]);
GO

-- Quick sanity print of what now exists on the table.
SELECT i.name AS IndexName, i.type_desc, i.is_unique, i.has_filter, i.filter_definition
FROM sys.indexes i
WHERE i.object_id = OBJECT_ID('dbo.QuotePerformanceTest')
  AND i.type > 0
ORDER BY i.type, i.name;
GO
