-- Measurement script. Run BEFORE and AFTER index creation.
-- Result rows are consumed into local sinks so the only meaningful stdout is
-- STATISTICS IO / TIME output (logical reads, scan count, CPU/elapsed).
USE QuotesDb;
GO

DBCC DROPCLEANBUFFERS;        -- clear data cache for a fair logical-read picture
DBCC FREEPROCCACHE;           -- force fresh plan compile each run
GO

SET STATISTICS IO ON;
SET STATISTICS TIME ON;
GO

PRINT '----- Q1: Single-row lookup by Id (target: clustered index seek) -----';
DECLARE @sink_id INT, @sink_author NVARCHAR(200), @sink_cat NVARCHAR(50), @sink_dt DATETIME2(0);
SELECT @sink_id = Id, @sink_author = Author, @sink_cat = Category, @sink_dt = CreatedDate
FROM dbo.QuotePerformanceTest
WHERE Id = 75123;
GO

PRINT '----- Q2: All quotes by a specific author (target: NC seek on Author) -----';
DECLARE @c2 INT;
SELECT @c2 = COUNT_BIG(*)
FROM dbo.QuotePerformanceTest
WHERE Author = N'Marcus Aurelius'
  AND IsDeleted = 0;
PRINT CONCAT('Rows matched: ', @c2);
GO

PRINT '----- Q3: Latest 50 quotes in a Category (target: NC seek on Category+CreatedDate DESC) -----';
DECLARE @sink_a NVARCHAR(200), @sink_t NVARCHAR(1000), @sink_d DATETIME2(0);
SELECT TOP 50 @sink_a = Author, @sink_t = [Text], @sink_d = CreatedDate
FROM dbo.QuotePerformanceTest
WHERE Category = N'Wisdom'
  AND CreatedDate >= '2025-01-01'
ORDER BY CreatedDate DESC;
GO

SET STATISTICS IO OFF;
SET STATISTICS TIME OFF;
GO
