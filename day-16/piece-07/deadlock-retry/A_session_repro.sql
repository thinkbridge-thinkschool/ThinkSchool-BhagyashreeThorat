-- ============================================================
-- SESSION A  --  REPRODUCE deadlock (BROKEN ordering)
-- Two resources: DeadlockDemo row Id=1 and row Id=2
-- Lock order for A:  Id=1  THEN  Id=2
-- ============================================================
SET NOCOUNT ON;
PRINT 'A: start  spid=' + CAST(@@SPID AS varchar(10));

BEGIN TRAN;

    -- Take exclusive lock on resource #1 (row Id=1)
    UPDATE dbo.DeadlockDemo SET Value = Value + 1 WHERE Id = 1;
    PRINT 'A: locked Id=1';

    -- Hold long enough for B to grab the opposite row, guaranteeing a cycle
    WAITFOR DELAY '00:00:04';

    -- Now reach for resource #2 (row Id=2) -- B already holds it -> WAIT
    PRINT 'A: requesting Id=2 ...';
    UPDATE dbo.DeadlockDemo SET Value = Value + 1 WHERE Id = 2;
    PRINT 'A: locked Id=2';

COMMIT;
PRINT 'A: committed OK';
