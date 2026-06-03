-- ============================================================
-- SESSION A  --  FIXED  (consistent lock ordering)
-- Resources unchanged: DeadlockDemo row Id=1 and row Id=2.
-- RULE: every transaction acquires rows in ASCENDING Id order
--       (Id=1 BEFORE Id=2), regardless of business intent.
-- Same WAITFOR as the repro -> proves the fix is ordering, not timing.
-- Hints: UPDLOCK (take the final X-equivalent lock up front, no S->X
--        upgrade deadlocks) + ROWLOCK (stay at row grain, no escalation).
-- ============================================================
SET NOCOUNT ON;
PRINT 'A(fixed): start  spid=' + CAST(@@SPID AS varchar(10));

BEGIN TRAN;

    -- Lowest Id first
    UPDATE dbo.DeadlockDemo WITH (UPDLOCK, ROWLOCK) SET Value = Value + 1 WHERE Id = 1;
    PRINT 'A(fixed): locked Id=1';

    WAITFOR DELAY '00:00:04';

    -- Then the next Id
    UPDATE dbo.DeadlockDemo WITH (UPDLOCK, ROWLOCK) SET Value = Value + 1 WHERE Id = 2;
    PRINT 'A(fixed): locked Id=2';

COMMIT;
PRINT 'A(fixed): committed OK';
