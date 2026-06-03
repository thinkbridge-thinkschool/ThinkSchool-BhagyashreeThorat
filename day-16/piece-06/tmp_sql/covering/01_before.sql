-- Day 8 Piece 5: Covering Index with INCLUDE — BEFORE
-- Existing index IX_QuotePerformanceTest_Author keys on Author, INCLUDEs
-- (Category, CreatedDate), filtered WHERE IsDeleted = 0.
-- The query below selects [Text], which is NOT in that index. The result is the
-- classic NC Seek + Key Lookup pattern — every matched row needs a lookup back
-- to the clustered index to fetch [Text].
--
-- A hint forces use of the non-clustered index. Without the hint the optimizer
-- prefers a clustered scan because the seek cardinality estimate (~2000 rows)
-- makes per-row Key Lookups look as expensive as just scanning the table.
-- Forcing the seek isolates the Key Lookup cost so the INCLUDE fix is visible.
USE QuotesDb;
GO

-- sqlcmd defaults to QUOTED_IDENTIFIER OFF; filtered indexes refuse to be used
-- without these SET options on.
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

PRINT '----- BEFORE: NC seek on Author + Key Lookup for [Text] -----';
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

-- Plan in text form (stand-in for SSMS Actual Execution Plan in headless run).
SET SHOWPLAN_TEXT ON;
GO
SELECT Id, Author, [Text], Category, CreatedDate
FROM dbo.QuotePerformanceTest WITH (INDEX (IX_QuotePerformanceTest_Author))
WHERE Author = N'Marcus Aurelius'
  AND IsDeleted = 0;
GO
SET SHOWPLAN_TEXT OFF;
GO
