SELECT Id, Author, substr(Text,1,60) AS Text, IsDeleted, OwnerId FROM Quotes ORDER BY Id;
GO
SELECT Id, Name, OwnerId FROM Collections ORDER BY Id;
GO
SELECT CollectionId, QuoteId, AddedAt FROM CollectionItems ORDER BY CollectionId, QuoteId;
GO
SELECT Id, Email FROM Users;
