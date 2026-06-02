-- ============================================================
-- SESSION B  --  REPRODUCE deadlock (BROKEN ordering)
-- Two resources: DeadlockDemo row Id=1 and row Id=2
-- Lock order for B:  Id=2  THEN  Id=1   <-- OPPOSITE of Session A
-- ============================================================
SET NOCOUNT ON;
PRINT 'B: start  spid=' + CAST(@@SPID AS varchar(10));

BEGIN TRAN;

    -- Take exclusive lock on resource #2 (row Id=2)
    UPDATE dbo.DeadlockDemo SET Value = Value + 1 WHERE Id = 2;
    PRINT 'B: locked Id=2';

    -- Hold long enough for A to grab the opposite row, guaranteeing a cycle
    WAITFOR DELAY '00:00:04';

    -- Now reach for resource #1 (row Id=1) -- A already holds it -> WAIT
    PRINT 'B: requesting Id=1 ...';
    UPDATE dbo.DeadlockDemo SET Value = Value + 1 WHERE Id = 1;
    PRINT 'B: locked Id=1';

COMMIT;
PRINT 'B: committed OK';
