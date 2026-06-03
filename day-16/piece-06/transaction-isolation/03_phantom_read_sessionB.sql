/* =====================================================================
   PHANTOM READ  —  SESSION B   (inserts a row into A's range)

   Run this in WINDOW 2 at STEP B2, i.e. AFTER Session A's first range read
   (A1) and BEFORE Session A's second range read (A3).

   This same Session B is used for both passes:
     - vs A in REPEATABLE READ -> this INSERT commits immediately (phantom)
     - vs A in SERIALIZABLE    -> this INSERT BLOCKS until A commits
   ===================================================================== */
USE QuotesDb;
GO

-------------------------------------------------------------------- STEP B2
-- New quote by the SAME author -> it falls inside Session A's range predicate.
INSERT INTO dbo.Quotes (Author, Text, IsDeleted, OwnerId)
VALUES (N'Marcus Aurelius',
        N'Waste no more time arguing about what a good man should be. Be one.',
        0, NULL);

SELECT 'B inserted phantom row' AS Stage, Id, Author, Text
FROM   dbo.Quotes WHERE Id = SCOPE_IDENTITY();
GO
-- >>> Now go back to Session A and run STEP A3 (its second range read). <<<
