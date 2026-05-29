<#
  run_demos.ps1 — reproduces all three read anomalies (and their prevention)
  LIVE against the project's SQL Server, capturing real interleaved output.

  How the two "windows" are automated:
    * Each scenario launches TWO concurrent sqlcmd sessions (A and B).
    * Ordering between them is enforced with WAITFOR DELAY instead of manual
      clicking: B pauses ~2s so it always acts in the middle of A's open
      transaction. The SQL is otherwise identical to the hand-run scripts.
    * The DB is reset from 00_setup.sql before every scenario.

  Usage:   powershell -ExecutionPolicy Bypass -File run_demos.ps1
#>

$ErrorActionPreference = 'Stop'
$here    = Split-Path -Parent $MyInvocation.MyCommand.Path
$sqlcmd  = "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\sqlcmd.exe"
$server  = "localhost,1433"
$baseArg = @("-S", $server, "-U", "sa", "-P", "YourStrong@Passw0rd", "-C", "-d", "QuotesDb", "-W", "-s", " | ")
$transcript = Join-Path $here "_capture_output.txt"
if (Test-Path $transcript) { Remove-Item $transcript }

function Write-Both([string]$text) { $text | Tee-Object -FilePath $transcript -Append }

function Reset-Db {
    & $sqlcmd -S $server -U sa -P "YourStrong@Passw0rd" -C -i (Join-Path $here "00_setup.sql") | Out-Null
}

function Invoke-Scenario {
    param([string]$Title, [string]$SqlA, [string]$SqlB)

    Reset-Db
    $fA = [IO.Path]::GetTempFileName(); $oA = [IO.Path]::GetTempFileName(); $eA = [IO.Path]::GetTempFileName()
    $fB = [IO.Path]::GetTempFileName(); $oB = [IO.Path]::GetTempFileName(); $eB = [IO.Path]::GetTempFileName()
    Set-Content -Path $fA -Value $SqlA -Encoding UTF8
    Set-Content -Path $fB -Value $SqlB -Encoding UTF8

    # Launch both sessions as close together as possible; WAITFOR handles ordering.
    $pA = Start-Process -FilePath $sqlcmd -ArgumentList ($baseArg + @("-i", $fA)) -NoNewWindow -PassThru -RedirectStandardOutput $oA -RedirectStandardError $eA
    $pB = Start-Process -FilePath $sqlcmd -ArgumentList ($baseArg + @("-i", $fB)) -NoNewWindow -PassThru -RedirectStandardOutput $oB -RedirectStandardError $eB
    $pA.WaitForExit(); $pB.WaitForExit()

    Write-Both ""
    Write-Both ("=" * 78)
    Write-Both $Title
    Write-Both ("=" * 78)
    Write-Both "----- SESSION A output ------------------------------------------------"
    Write-Both ((Get-Content $oA -Raw).Trim())
    $errA = (Get-Content $eA -Raw).Trim(); if ($errA) { Write-Both "  [A stderr] $errA" }
    Write-Both "----- SESSION B output ------------------------------------------------"
    Write-Both ((Get-Content $oB -Raw).Trim())
    $errB = (Get-Content $eB -Raw).Trim(); if ($errB) { Write-Both "  [B stderr] $errB" }

    Remove-Item $fA,$oA,$eA,$fB,$oB,$eB -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------- DIRTY READ
$dirtyA = @'
SET NOCOUNT ON;
BEGIN TRANSACTION;
UPDATE dbo.Quotes SET Text = N'!!! UNCOMMITTED EDIT by Session A !!!' WHERE Id = 4;
WAITFOR DELAY '00:00:04';
ROLLBACK TRANSACTION;
SELECT 'A after ROLLBACK (real value)' AS Stage, Id, CAST(Text AS NVARCHAR(60)) AS Text FROM dbo.Quotes WHERE Id = 4;
'@

$dirtyB_anom = @'
SET NOCOUNT ON;
WAITFOR DELAY '00:00:02';
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
SELECT 'B dirty read (READ UNCOMMITTED)' AS Stage, Id, CAST(Text AS NVARCHAR(60)) AS Text FROM dbo.Quotes WHERE Id = 4;
'@

$dirtyB_prev = @'
SET NOCOUNT ON;
WAITFOR DELAY '00:00:01';
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
DECLARE @t0 datetime2 = SYSDATETIME();
SELECT 'B clean read (READ COMMITTED)' AS Stage, Id, CAST(Text AS NVARCHAR(60)) AS Text FROM dbo.Quotes WHERE Id = 4;
SELECT 'B blocked on lock for (ms)' AS Note, DATEDIFF(ms, @t0, SYSDATETIME()) AS WaitedMs;
'@

Invoke-Scenario "DIRTY READ  —  ANOMALY (B uses READ UNCOMMITTED)"        $dirtyA $dirtyB_anom
Invoke-Scenario "DIRTY READ  —  PREVENTED (B uses READ COMMITTED)"        $dirtyA $dirtyB_prev

# ------------------------------------------------------- NON-REPEATABLE READ
$nrA_anom = @'
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
BEGIN TRANSACTION;
SELECT 'A read #1 (READ COMMITTED)' AS Stage, Id, CAST(Text AS NVARCHAR(60)) AS Text FROM dbo.Quotes WHERE Id = 3;
WAITFOR DELAY '00:00:04';
SELECT 'A read #2 (READ COMMITTED)' AS Stage, Id, CAST(Text AS NVARCHAR(60)) AS Text FROM dbo.Quotes WHERE Id = 3;
COMMIT TRANSACTION;
'@

$nrA_prev = @'
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;
BEGIN TRANSACTION;
SELECT 'A read #1 (REPEATABLE READ)' AS Stage, Id, CAST(Text AS NVARCHAR(60)) AS Text FROM dbo.Quotes WHERE Id = 3;
WAITFOR DELAY '00:00:04';
SELECT 'A read #2 (REPEATABLE READ)' AS Stage, Id, CAST(Text AS NVARCHAR(60)) AS Text FROM dbo.Quotes WHERE Id = 3;
COMMIT TRANSACTION;
'@

$nrB = @'
SET NOCOUNT ON;
WAITFOR DELAY '00:00:02';
DECLARE @t0 datetime2 = SYSDATETIME();
UPDATE dbo.Quotes SET Text = N'Sometimes you will never know the value of a moment until it becomes a memory.' WHERE Id = 3;
SELECT 'B committed UPDATE on Id=3' AS Stage, DATEDIFF(ms, @t0, SYSDATETIME()) AS BlockedMs;
'@

Invoke-Scenario "NON-REPEATABLE READ  —  ANOMALY (A uses READ COMMITTED)"   $nrA_anom $nrB
Invoke-Scenario "NON-REPEATABLE READ  —  PREVENTED (A uses REPEATABLE READ)" $nrA_prev $nrB

# -------------------------------------------------------------- PHANTOM READ
$phA_anom = @'
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;
BEGIN TRANSACTION;
SELECT 'A range COUNT #1 (REPEATABLE READ)' AS Stage, COUNT(*) AS Rows FROM dbo.Quotes WHERE Author = N'Marcus Aurelius' AND IsDeleted = 0;
WAITFOR DELAY '00:00:04';
SELECT 'A range COUNT #2 (REPEATABLE READ)' AS Stage, COUNT(*) AS Rows FROM dbo.Quotes WHERE Author = N'Marcus Aurelius' AND IsDeleted = 0;
COMMIT TRANSACTION;
'@

$phA_prev = @'
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;
SELECT 'A range COUNT #1 (SERIALIZABLE)' AS Stage, COUNT(*) AS Rows FROM dbo.Quotes WHERE Author = N'Marcus Aurelius' AND IsDeleted = 0;
WAITFOR DELAY '00:00:04';
SELECT 'A range COUNT #2 (SERIALIZABLE)' AS Stage, COUNT(*) AS Rows FROM dbo.Quotes WHERE Author = N'Marcus Aurelius' AND IsDeleted = 0;
COMMIT TRANSACTION;
'@

$phB = @'
SET NOCOUNT ON;
WAITFOR DELAY '00:00:02';
DECLARE @t0 datetime2 = SYSDATETIME();
INSERT INTO dbo.Quotes (Author, Text, IsDeleted, OwnerId)
VALUES (N'Marcus Aurelius', N'Waste no more time arguing about what a good man should be. Be one.', 0, NULL);
SELECT 'B inserted matching row' AS Stage, DATEDIFF(ms, @t0, SYSDATETIME()) AS BlockedMs;
'@

Invoke-Scenario "PHANTOM READ  —  ANOMALY (A uses REPEATABLE READ)"  $phA_anom $phB
Invoke-Scenario "PHANTOM READ  —  PREVENTED (A uses SERIALIZABLE)"   $phA_prev $phB

Reset-Db   # leave the table in its clean seeded state
Write-Both ""
Write-Both "Done. Transcript saved to $transcript"
