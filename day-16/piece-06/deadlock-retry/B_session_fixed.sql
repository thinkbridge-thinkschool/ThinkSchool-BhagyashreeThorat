-- ============================================================
-- SESSION B  --  FIXED  (consistent lock ordering)
-- Business intent of B still touches BOTH rows, but it now follows
-- the SAME ascending-Id rule as Session A: Id=1 BEFORE Id=2.
-- This is the ONLY change vs. the broken repro -- the opposite
-- (Id=2 -> Id=1) order is gone, so the circular wait cannot form.
-- Same WAITFOR as the repro -> proves the fix is ordering, not timing.
-- ============================================================
SET NOCOUNT ON;
PRINT 'B(fixed): start  spid=' + CAST(@@SPID AS varchar(10));

BEGIN TRAN;

    -- Lowest Id first (was Id=2 first in the broken version)
    UPDATE dbo.DeadlockDemo WITH (UPDLOCK, ROWLOCK) SET Value = Value + 1 WHERE Id = 1;
    PRINT 'B(fixed): locked Id=1';

    WAITFOR DELAY '00:00:04';

    -- Then the next Id
    UPDATE dbo.DeadlockDemo WITH (UPDLOCK, ROWLOCK) SET Value = Value + 1 WHERE Id = 2;
    PRINT 'B(fixed): locked Id=2';

COMMIT;
PRINT 'B(fixed): committed OK';
