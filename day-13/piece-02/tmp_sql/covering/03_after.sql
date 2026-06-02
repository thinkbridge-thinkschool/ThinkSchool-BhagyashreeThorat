-- Day 8 Piece 5: Covering Index with INCLUDE — AFTER
-- Same query as the BEFORE step (with the same hint so the comparison is
-- apples-to-apples on the same index). The Key Lookup should be gone:
-- [Text] is now in the non-clustered leaf, so the seek alone is enough.
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

DBCC DROPCLEANBUFFERS;
DBCC FREEPROCCACHE;
GO

SET STATISTICS IO ON;
SET STATISTICS TIME ON;
GO

PRINT '----- AFTER: covering NC seek, no Key Lookup -----';
DECLARE @sink_id INT, @sink_author NVARCHAR(200), @sink_text NVARCHAR(1000),
        @sink_cat NVARCHAR(50), @sink_dt DATETIME2(0);
SELECT @sink_id     = Id,
       @sink_author = Author,
       @sink_text   = [Text],
       @sink_cat    = Category,
       @sink_dt     = CreatedDate
FROM dbo.QuotePerformanceTest WITH (INDEX (IX_QuotePerformanceTest_Author))
WHERE Author = N'Marcus Aurelius'
  AND IsDeleted = 0;
GO

SET STATISTICS IO OFF;
SET STATISTICS TIME OFF;
GO

SET SHOWPLAN_TEXT ON;
GO
SELECT Id, Author, [Text], Category, CreatedDate
FROM dbo.QuotePerformanceTest WITH (INDEX (IX_QuotePerformanceTest_Author))
WHERE Author = N'Marcus Aurelius'
  AND IsDeleted = 0;
GO
SET SHOWPLAN_TEXT OFF;
GO
