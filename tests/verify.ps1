param(
    [string]$Executable,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../runs/verification'),
    [switch]$Warp
)

$ErrorActionPreference = 'Stop'
if (-not $Executable) {
    $Executable = & (Join-Path $PSScriptRoot '../build.ps1')
    if ($LASTEXITCODE -ne 0) { throw "Build exited with code $LASTEXITCODE." }
}
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$adapter = @()
if ($Warp) { $adapter += '--warp' }
& $Executable @adapter --probe --out $OutputDirectory | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Direct3D 12 probe exited with code $LASTEXITCODE." }
foreach ($count in 1000,10000) {
    $folder = Join-Path $OutputDirectory "objects-$count"
    & $Executable @adapter --test --objects $count --out $folder | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Image verification failed at $count objects." }
    $result = Get-Content -LiteralPath (Join-Path $folder 'verification.json') -Raw | ConvertFrom-Json
    if (-not $result.Passed -or $result.Checks.Count -ne 6) { throw 'Incomplete verification output.' }
    if ($result.Hardware.SoftwareAdapter -ne $Warp.IsPresent) { throw 'Unexpected adapter type.' }
    Write-Output "$count objects: 6 identical images on $($result.Hardware.Adapter)."
}
$smoke = Join-Path $OutputDirectory 'benchmark-smoke'
& $Executable @adapter --objects 10000 --frames 30 --warmup 5 --out $smoke | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Benchmark smoke test exited with code $LASTEXITCODE." }
$report = Get-Content -LiteralPath (Join-Path $smoke 'benchmark.json') -Raw | ConvertFrom-Json
$csv = Import-Csv -LiteralPath (Join-Path $smoke 'frames.csv')
if ($report.Frames.Count -ne 90 -or $csv.Count -ne 90 -or $report.Summary.Count -ne 3) {
    throw 'Missing frames or rendering path in the benchmark output.'
}
foreach ($path in 'direct','indirect','expanded') {
    $row = @($report.Summary | Where-Object Path -eq $path)
    if ($row.Count -ne 1 -or $row[0].Count -ne 30 -or $row[0].AverageGpuRasterMs -le 0) {
        throw "Unexpected benchmark summary for $path."
    }
}
Write-Output '90 timed frames and all three paths verified in JSON and CSV.'
