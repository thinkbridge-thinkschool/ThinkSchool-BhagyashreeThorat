-- Day-11 perf lab — capture the SQL execution plan + IO/time stats.
--
-- HOW TO USE (SSMS, connected to your Azure SQL QuotesDb):
--   1. Toolbar: click "Include Actual Execution Plan" (Ctrl+M).
--   2. Run this whole script once with NO covering index (the "before").
--   3. Run Scripts/add-covering-index.sql, then run this again (the "after").
--   4. Save each .sqlplan (right-click the plan tab → Save Execution Plan As).
--   5. Compare: Clustered Index SCAN (before) vs Index SEEK (after), and the
--      logical reads in the Messages tab.

SET STATISTICS IO, TIME ON;

-- (A) The shape of ONE iteration of the N+1 loop — what the slow endpoint fires
--     once per author. Pick any AuthorId that exists.
DECLARE @authorId int = (SELECT TOP 1 Id FROM dbo.Authors ORDER BY Id);

SELECT q.Id, q.Author, q.[Text], q.IsDeleted, q.OwnerId, q.AuthorId
FROM dbo.Quotes AS q
WHERE q.AuthorId = @authorId AND q.IsDeleted = 0;

-- (B) The single set-based query behind /summary-fast — the good version.
SELECT a.Id AS AuthorId, a.Name, COUNT(q.Id) AS QuoteCount
FROM dbo.Authors AS a
LEFT JOIN dbo.Quotes AS q
    ON q.AuthorId = a.Id AND q.IsDeleted = 0
GROUP BY a.Id, a.Name
ORDER BY a.Name;

SET STATISTICS IO, TIME OFF;
