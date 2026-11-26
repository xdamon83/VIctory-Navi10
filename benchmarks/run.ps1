param(
    [string]$BuildDirectory = (Join-Path $PSScriptRoot '../build'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../runs'),
    [string]$Executable,
    [ValidateRange(1,10000)][int]$Frames = 150,
    [ValidateRange(0,10000)][int]$StabilityFrames = 2000
)

$ErrorActionPreference = 'Stop'
$exe = $Executable
if (-not $exe) {
    $exe = & (Join-Path $PSScriptRoot '../build.ps1') -OutputDirectory $BuildDirectory
    if ($LASTEXITCODE -ne 0) { throw "Build failed with code $LASTEXITCODE." }
}
& $exe --probe --out $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw "Hardware probe failed with code $LASTEXITCODE." }
foreach ($count in 1000,10000,50000,100000) {
    $folder = Join-Path $OutputDirectory "objects-$count"
    & $exe --test --objects $count --out $folder
    if ($LASTEXITCODE -ne 0) { throw "Image verification failed for $count objects." }
    & $exe --objects $count --frames $Frames --out $folder
    if ($LASTEXITCODE -ne 0) { throw "Benchmark failed for $count objects." }
}
if ($StabilityFrames -gt 0) {
    & $exe --objects 100000 --frames $StabilityFrames --out (Join-Path $OutputDirectory 'stability')
    if ($LASTEXITCODE -ne 0) { throw "Stability run failed with code $LASTEXITCODE." }
}
