-- Internal helper: roll IX_QuotePerformanceTest_Author back to its pre-piece-5
-- shape so we can capture a clean BEFORE output, then 02_covering_ddl.sql will
-- re-apply the Text INCLUDE for the AFTER state.
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
    INCLUDE (Category, CreatedDate)
    WHERE IsDeleted = 0
    WITH (DROP_EXISTING = ON);
GO
