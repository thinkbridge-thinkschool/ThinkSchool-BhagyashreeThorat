-- Session A

BEGIN TRAN;

UPDATE DeadlockDemo
SET Value=Value+1
WHERE Id=1;

WAITFOR DELAY '00:00:10';

UPDATE DeadlockDemo
SET Value=Value+1
WHERE Id=2;

COMMIT;


-- Session B

BEGIN TRAN;

UPDATE DeadlockDemo
SET Value=Value+1
WHERE Id=2;

WAITFOR DELAY '00:00:10';

UPDATE DeadlockDemo
SET Value=Value+1
WHERE Id=1;

COMMIT;



-- Deadlock victim output

-- Transaction was deadlocked...



-- Fixed Session A

BEGIN TRAN;

UPDATE DeadlockDemo
SET Value=Value+1
WHERE Id=1;

WAITFOR DELAY '00:00:05';

UPDATE DeadlockDemo
SET Value=Value+1
WHERE Id=2;

COMMIT;



-- Fixed Session B

BEGIN TRAN;

UPDATE DeadlockDemo
SET Value=Value+1
WHERE Id=1;

WAITFOR DELAY '00:00:05';

UPDATE DeadlockDemo
SET Value=Value+1
WHERE Id=2;

COMMIT;



-- Explanation

-- Consistent lock ordering prevents circular waiting:
-- Using the same lock order prevents circular wait conditions because both transactions request resources in the same sequence.