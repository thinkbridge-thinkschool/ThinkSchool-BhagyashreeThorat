/* =====================================================================
   00_setup.sql  —  One-time setup for the isolation-level demos.

   Creates the QuotesDb database (matching the project connection string)
   and a dbo.Quotes table that MIRRORS the existing EF Core schema exactly:

        Id        int IDENTITY  PRIMARY KEY
        Author    nvarchar(max) NOT NULL
        Text      nvarchar(max) NOT NULL
        IsDeleted bit           NOT NULL
        OwnerId   int           NULL

   No application code, repositories, or services are touched. This is the
   minimal data setup the assignment allows ("use existing Quotes tables;
   create a small test table only if needed for the experiments").

   Run ONCE before the demos. Safe to re-run (it resets the demo rows).
   ===================================================================== */

IF DB_ID('QuotesDb') IS NULL
    CREATE DATABASE QuotesDb;
GO

USE QuotesDb;
GO

IF OBJECT_ID('dbo.Quotes', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Quotes
    (
        Id        INT           IDENTITY(1,1) NOT NULL,
        Author    NVARCHAR(MAX) NOT NULL,
        Text      NVARCHAR(MAX) NOT NULL,
        IsDeleted BIT           NOT NULL,
        OwnerId   INT           NULL,
        CONSTRAINT PK_Quotes PRIMARY KEY CLUSTERED (Id)
    );
END
GO

/* Reset to a known, realistic starting state every time we set up. */
DELETE FROM dbo.Quotes;
SET IDENTITY_INSERT dbo.Quotes ON;

INSERT INTO dbo.Quotes (Id, Author, Text, IsDeleted, OwnerId) VALUES
 (1, N'Marcus Aurelius', N'You have power over your mind - not outside events. Realize this, and you will find strength.', 0, NULL),
 (2, N'Marcus Aurelius', N'The happiness of your life depends upon the quality of your thoughts.',                          0, NULL),
 (3, N'Seneca',          N'Luck is what happens when preparation meets opportunity.',                                       0, NULL),
 (4, N'APJ Abdul Kalam', N'Dream is not that which you see while sleeping, it is something that does not let you sleep.',    0, NULL),
 (5, N'Confucius',       N'It does not matter how slowly you go as long as you do not stop.',                               0, NULL);

SET IDENTITY_INSERT dbo.Quotes OFF;
GO

SELECT Id, Author, LEFT(Text, 55) AS Text, IsDeleted, OwnerId
FROM   dbo.Quotes
ORDER BY Id;
GO
