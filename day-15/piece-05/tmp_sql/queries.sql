-- Query A: Authors with quotes but no tags (no CollectionItem membership)
-- "Tag" is mapped to Collection membership via CollectionItems (closest existing tag-like structure).
SELECT DISTINCT Author
FROM   Quotes
WHERE  IsDeleted = 0
EXCEPT
SELECT DISTINCT q.Author
FROM   Quotes q
JOIN   CollectionItems ci ON ci.QuoteId = q.Id
WHERE  q.IsDeleted = 0
ORDER BY Author;
GO

-- Query B: Authors present in BOTH the "classic" and "modern" sets.
-- Classic / Modern do not exist as tables. Mapped to two derived author lists
-- against the EXISTING Author column (real values, no fake tables).
SELECT DISTINCT Author
FROM   Quotes
WHERE  IsDeleted = 0
  AND  Author IN ('APJ Abdul Kalam','Confucius','Seneca','Swami Vivekananda','Mother Teresa')
INTERSECT
SELECT DISTINCT Author
FROM   Quotes
WHERE  IsDeleted = 0
  AND  Author IN ('APJ Abdul Kalam','Steve Jobs','Albert Einstein','Nelson Mandela','Walt Disney','Mother Teresa')
ORDER BY Author;
GO

-- Query C: Combined distinct "tag" list across the two categories (Classic + Modern).
-- Authors function as the per-quote "tag" here (no Tags table). Each category's tag set
-- is the distinct list of authors in that category; UNION yields the combined distinct list.
SELECT DISTINCT Author
FROM   Quotes
WHERE  IsDeleted = 0
  AND  Author IN ('APJ Abdul Kalam','Confucius','Seneca','Swami Vivekananda','Mother Teresa')
UNION
SELECT DISTINCT Author
FROM   Quotes
WHERE  IsDeleted = 0
  AND  Author IN ('APJ Abdul Kalam','Steve Jobs','Albert Einstein','Nelson Mandela','Walt Disney','Mother Teresa')
ORDER BY Author;
