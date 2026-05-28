-- Day 8 Piece 4: Clustered vs Non-Clustered Index Experiment
-- Uses the existing QuotesDb. Creates an isolated experiment table.
USE QuotesDb;
GO

IF OBJECT_ID('dbo.QuotePerformanceTest', 'U') IS NOT NULL
    DROP TABLE dbo.QuotePerformanceTest;
GO

-- Heap to begin with (no PK, no indexes) so BEFORE measurements
-- reflect a full table scan and are directly comparable to AFTER.
CREATE TABLE dbo.QuotePerformanceTest
(
    Id          INT            NOT NULL,
    Author      NVARCHAR(200)  NOT NULL,
    Text        NVARCHAR(1000) NOT NULL,
    Category    NVARCHAR(50)   NOT NULL,
    CreatedDate DATETIME2(0)   NOT NULL,
    IsDeleted   BIT            NOT NULL
);
GO

-- Lookup tables to keep INSERT readable.
DECLARE @authors TABLE (idx INT PRIMARY KEY, name NVARCHAR(200));
INSERT INTO @authors(idx, name) VALUES
 (0,'Marcus Aurelius'),(1,'Seneca'),(2,'Epictetus'),(3,'Lao Tzu'),(4,'Confucius'),
 (5,'Albert Einstein'),(6,'Maya Angelou'),(7,'Mark Twain'),(8,'Oscar Wilde'),(9,'Friedrich Nietzsche'),
 (10,'Virginia Woolf'),(11,'Jane Austen'),(12,'Leo Tolstoy'),(13,'Fyodor Dostoevsky'),(14,'Ralph Waldo Emerson'),
 (15,'Henry David Thoreau'),(16,'Rumi'),(17,'Kahlil Gibran'),(18,'Carl Jung'),(19,'Sigmund Freud'),
 (20,'Winston Churchill'),(21,'Abraham Lincoln'),(22,'Nelson Mandela'),(23,'Mahatma Gandhi'),(24,'Martin Luther King Jr.');

DECLARE @categories TABLE (idx INT PRIMARY KEY, name NVARCHAR(50));
INSERT INTO @categories(idx, name) VALUES
 (0,'Wisdom'),(1,'Stoicism'),(2,'Motivation'),(3,'Love'),(4,'Life'),
 (5,'Philosophy'),(6,'Humor'),(7,'Success'),(8,'Friendship'),(9,'Time');

;WITH
e1(n) AS (SELECT 1 UNION ALL SELECT 1 UNION ALL SELECT 1 UNION ALL SELECT 1 UNION ALL SELECT 1
          UNION ALL SELECT 1 UNION ALL SELECT 1 UNION ALL SELECT 1 UNION ALL SELECT 1 UNION ALL SELECT 1),
e2(n) AS (SELECT 1 FROM e1 a CROSS JOIN e1 b),
e4(n) AS (SELECT 1 FROM e2 a CROSS JOIN e2 b),
nums  AS (SELECT TOP (100000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM e4 a CROSS JOIN e4 b)
INSERT INTO dbo.QuotePerformanceTest (Id, Author, Text, Category, CreatedDate, IsDeleted)
SELECT
    n.n AS Id,
    a.name AS Author,
    CONCAT(
        'Quote #', n.n, ' by ', a.name,
        ' on ', c.name,
        ' - reflection ', (n.n % 997),
        '. ', REPLICATE('lorem ipsum dolor sit amet ', 3)
    ) AS Text,
    c.name AS Category,
    DATEADD(MINUTE, -(n.n % 525600), CAST('2026-01-01' AS DATETIME2(0))) AS CreatedDate,
    CASE WHEN n.n % 50 = 0 THEN 1 ELSE 0 END AS IsDeleted
FROM nums n
INNER JOIN @authors    a ON a.idx = (n.n * 31)  % 25
INNER JOIN @categories c ON c.idx = (n.n * 17)  % 10;
GO

SELECT COUNT(*) AS RowCountInserted FROM dbo.QuotePerformanceTest;
SELECT TOP 5 * FROM dbo.QuotePerformanceTest ORDER BY Id;
GO
