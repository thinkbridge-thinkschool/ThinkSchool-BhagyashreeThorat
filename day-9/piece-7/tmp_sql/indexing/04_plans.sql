-- Confirm the indexes are actually being used (Actual Execution Plan substitute).
USE QuotesDb;
GO
SET SHOWPLAN_TEXT ON;
GO

SELECT Id, Author, Category, CreatedDate
FROM dbo.QuotePerformanceTest
WHERE Id = 75123;
GO

SELECT COUNT_BIG(*) AS RowsForAuthor
FROM dbo.QuotePerformanceTest
WHERE Author = N'Marcus Aurelius'
  AND IsDeleted = 0;
GO

SELECT TOP 50 Id, Author, Category, CreatedDate
FROM dbo.QuotePerformanceTest
WHERE Category = N'Wisdom'
  AND CreatedDate >= '2025-01-01'
ORDER BY CreatedDate DESC;
GO

SET SHOWPLAN_TEXT OFF;
GO
