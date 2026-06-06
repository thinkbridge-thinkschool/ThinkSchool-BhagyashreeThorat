# Day-11 perf lab — load test the slow and fast endpoints with bombardier.
#
# Install bombardier first (pick one):
#   winget install codesenberg.bombardier
#   choco install bombardier
#   go install github.com/codesenberg/bombardier@latest
#
# Usage:
#   .\load-test.ps1 -BaseUrl https://your-app.azurecontainerapps.io
#   .\load-test.ps1 -BaseUrl http://localhost:5000 -Connections 50 -Duration 30s

param(
    [Parameter(Mandatory = $true)] [string]$BaseUrl,
    [int]$Connections = 50,
    [string]$Duration = "30s"
)

$outDir = Join-Path $PSScriptRoot "..\PerfResults"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function Invoke-Bench($name, $path) {
    $url = "$BaseUrl$path"
    Write-Host "`n=== $name -> $url ===" -ForegroundColor Cyan
    # -l prints the latency distribution (p50/p75/p90/p95/p99)
    $out = bombardier -c $Connections -d $Duration -l --print=result $url
    $out | Tee-Object -FilePath (Join-Path $outDir "$name.txt")
}

Invoke-Bench "slow" "/api/authors/summary"
Invoke-Bench "fast" "/api/authors/summary-fast"

Write-Host "`nResults saved under PerfResults\. Record the p50/p99 lines in PERF-REPORT.md." -ForegroundColor Green
