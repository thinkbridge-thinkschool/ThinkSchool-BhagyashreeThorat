-- Write-side cost comparison.
-- Insert the same 5,000 rows into two tables: one with NO indexes (heap),
-- one with the same 3 indexes as our experiment table. Compare logical writes & CPU.
USE QuotesDb;
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.QPT_NoIndex','U') IS NOT NULL DROP TABLE dbo.QPT_NoIndex;
IF OBJECT_ID('dbo.QPT_Indexed','U') IS NOT NULL DROP TABLE dbo.QPT_Indexed;
GO

CREATE TABLE dbo.QPT_NoIndex (
    Id INT NOT NULL, Author NVARCHAR(200) NOT NULL, [Text] NVARCHAR(1000) NOT NULL,
    Category NVARCHAR(50) NOT NULL, CreatedDate DATETIME2(0) NOT NULL, IsDeleted BIT NOT NULL
);

CREATE TABLE dbo.QPT_Indexed (
    Id INT NOT NULL, Author NVARCHAR(200) NOT NULL, [Text] NVARCHAR(1000) NOT NULL,
    Category NVARCHAR(50) NOT NULL, CreatedDate DATETIME2(0) NOT NULL, IsDeleted BIT NOT NULL
);
CREATE UNIQUE CLUSTERED INDEX CIX_QPT_Indexed_Id ON dbo.QPT_Indexed (Id);
CREATE NONCLUSTERED INDEX IX_QPT_Indexed_Author
    ON dbo.QPT_Indexed (Author) INCLUDE (Category, CreatedDate) WHERE IsDeleted = 0;
CREATE NONCLUSTERED INDEX IX_QPT_Indexed_Category_CreatedDate
    ON dbo.QPT_Indexed (Category, CreatedDate DESC) INCLUDE (Author, [Text]);
GO

-- Build a 5,000-row source set
IF OBJECT_ID('tempdb..#src','U') IS NOT NULL DROP TABLE #src;
;WITH e1(n) AS (SELECT 1 UNION ALL SELECT 1 UNION ALL SELECT 1 UNION ALL SELECT 1 UNION ALL SELECT 1
                UNION ALL SELECT 1 UNION ALL SELECT 1 UNION ALL SELECT 1 UNION ALL SELECT 1 UNION ALL SELECT 1),
      e2(n) AS (SELECT 1 FROM e1 a CROSS JOIN e1 b),
      e4(n) AS (SELECT 1 FROM e2 a CROSS JOIN e2 b),
      nums AS (SELECT TOP (5000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM e4 a CROSS JOIN e4 b)
SELECT
    200000 + n AS Id,
    CONCAT(N'WriteAuthor_', n % 25) AS Author,
    REPLICATE(N'payload ', 20) AS [Text],
    CONCAT(N'Cat_', n % 10) AS Category,
    DATEADD(MINUTE, -n, CAST('2026-01-01' AS DATETIME2(0))) AS CreatedDate,
    CAST(0 AS BIT) AS IsDeleted
INTO #src
FROM nums;
GO

SET STATISTICS IO ON;
SET STATISTICS TIME ON;
GO

PRINT '----- INSERT 5,000 rows into HEAP (no indexes) -----';
INSERT INTO dbo.QPT_NoIndex (Id, Author, [Text], Category, CreatedDate, IsDeleted)
SELECT Id, Author, [Text], Category, CreatedDate, IsDeleted FROM #src;
GO

PRINT '----- INSERT 5,000 rows into INDEXED table (1 clustered + 2 NC) -----';
INSERT INTO dbo.QPT_Indexed (Id, Author, [Text], Category, CreatedDate, IsDeleted)
SELECT Id, Author, [Text], Category, CreatedDate, IsDeleted FROM #src;
GO

SET STATISTICS IO OFF;
SET STATISTICS TIME OFF;
GO

-- Clean up artifacts so the experiment table is the only thing left.
DROP TABLE dbo.QPT_NoIndex;
DROP TABLE dbo.QPT_Indexed;
GO
