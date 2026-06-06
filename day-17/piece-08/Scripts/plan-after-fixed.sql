-- Day-11 p11 — AFTER plan for the FIXED /api/authors/summary endpoint.
-- This is the single set-based statement EF emits for the projection (one
-- round-trip; the two navigation sub-aggregations become correlated subqueries).
-- Captured WITH the covering index IX_Quotes_AuthorId (AuthorId) INCLUDE (IsDeleted, Text).
SET STATISTICS IO, TIME ON;
SET STATISTICS XML ON;

SELECT [a].[Id], [a].[Name],
    (SELECT COUNT(*) FROM [Quotes] AS [q]
       WHERE [a].[Id] = [q].[AuthorId] AND [q].[IsDeleted] = CAST(0 AS bit)) AS QuoteCount,
    (SELECT TOP(1) [q0].[Text] FROM [Quotes] AS [q0]
       WHERE [a].[Id] = [q0].[AuthorId] AND [q0].[IsDeleted] = CAST(0 AS bit)
       ORDER BY [q0].[Id] DESC) AS LatestQuote
FROM [Authors] AS [a]
ORDER BY [a].[Name];

SET STATISTICS XML OFF;
SET STATISTICS IO, TIME OFF;
