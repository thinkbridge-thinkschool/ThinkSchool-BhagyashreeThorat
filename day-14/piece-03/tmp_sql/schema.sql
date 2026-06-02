SELECT name, type FROM sqlite_master WHERE type IN ('table','view') ORDER BY type, name;
GO
SELECT name FROM pragma_table_info('Quotes');
GO
SELECT name FROM pragma_table_info('Collections');
GO
SELECT name FROM pragma_table_info('CollectionItems');
GO
SELECT name FROM pragma_table_info('Users');
GO
SELECT COUNT(*) AS QuoteCount FROM Quotes;
GO
SELECT COUNT(*) AS CollectionCount FROM Collections;
GO
SELECT COUNT(*) AS CollectionItemCount FROM CollectionItems;
GO
SELECT COUNT(*) AS UserCount FROM Users;
