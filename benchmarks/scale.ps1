param(
    [string]$Executable,
    [string]$Out = (Join-Path $PSScriptRoot '../runs/v030-scale'),
    [ValidateSet('matrix','ab','prefix','memory','overlap','stability')][string]$Mode = 'matrix',
    [int]$Frames = 24,
    [int]$Warmup = 6,
    [int]$GroupSize = 64,
    [int]$StabilityTriangles = 100000,
    [ValidateRange(1,3)][int]$FramesInFlight = 1,
    [switch]$Prefix,
    [switch]$AnimateTiles
)

$ErrorActionPreference = 'Stop'
if (!$Executable) { $Executable = & (Join-Path $PSScriptRoot '../build.ps1') }
$Executable = (Resolve-Path -LiteralPath $Executable).Path

function Run-Scale([string]$Name, [int]$Triangles, [int]$Height, [int]$Group, [bool]$Batch, [int]$Count,
    [int]$Slots = 1, [bool]$Fast = $false, [bool]$Reuse = $false) {
    $folder = Join-Path $Out $Name
    $width = [int]($Height * 16 / 9)
    $flags = @('--meshlet-scale', "$Triangles", '--width', "$width", '--height', "$Height", '--frames', "$Count",
        '--warmup', "$Warmup", '--meshlet-group-size', "$Group", '--frames-in-flight', "$Slots", '--out', $folder)
    if ($Batch) { $flags += '--meshlet-batched' }
    if ($Fast) { $flags += '--meshlet-full-capacity' }
    if ($Reuse) { $flags += '--reuse-input-vertices' }
    if ($Prefix -and ($Mode -ne 'prefix' -or $Name -like '*-b*')) { $flags += '--meshlet-prefix' }
    if ($AnimateTiles) { $flags += '--animate-tiles' }
    & $Executable @flags | Out-Null
    if ($LASTEXITCODE) { throw "Scale run failed on $Name." }
    $result = Get-Content -LiteralPath (Join-Path $folder 'scale.json') -Raw | ConvertFrom-Json
    if (!$result.Passed -or $result.Frames.Count -ne $Count -or $result.Comparisons.Count -ne 4) {
        throw "Incomplete scale results on $Name."
    }
    $result.Frames | Export-Csv -LiteralPath (Join-Path $folder 'frames.csv') -NoTypeInformation
    $ms = @($result.Frames.TotalMs | Sort-Object)
    [pscustomobject]@{
        Run = $Name
        Frames = $Count
        MeanFps = [math]::Round($result.MeasuredFps, 2)
        FramesInFlight = $Slots
        P50Ms = $ms[[math]::Ceiling(0.5 * $Count) - 1]
        P95Ms = $ms[[math]::Ceiling(0.95 * $Count) - 1]
        P99Ms = $ms[[math]::Ceiling(0.99 * $Count) - 1]
        CpuSubmitMs = ($result.Frames.CpuSubmitMs | Measure-Object -Average).Average
        GpuComputeMs = ($result.Frames.GpuComputeMs | Measure-Object -Average).Average
        GpuRasterMs = ($result.Frames.GpuRasterMs | Measure-Object -Average).Average
        PeakRamBytes = ($result.Frames.WorkingSetBytes | Measure-Object -Maximum).Maximum
        PeakVramBytes = ($result.Frames.LocalVideoBytes | Measure-Object -Maximum).Maximum
        VisibleTriangles = [int]($result.Frames.VisibleTriangles | Measure-Object -Average).Average
        ExactComparisons = $result.Comparisons.Count
    }
}

$summary = Join-Path $Out 'summary.csv'
New-Item -ItemType Directory -Path $Out -Force | Out-Null
$results = if ($Mode -eq 'matrix') {
    foreach ($triangles in 10000, 100000, 500000, 1000000, 5000000) {
        foreach ($height in 540, 720, 1080) {
            Run-Scale "$triangles-$height" $triangles $height $GroupSize $true $Frames 1 $true $true
        }
    }
} elseif ($Mode -eq 'ab') {
    foreach ($triangles in 100000, 1000000) {
        Run-Scale "$triangles-a1" $triangles 720 $GroupSize $false $Frames
        Run-Scale "$triangles-b1" $triangles 720 $GroupSize $true $Frames
        Run-Scale "$triangles-b2" $triangles 720 $GroupSize $true $Frames
        Run-Scale "$triangles-a2" $triangles 720 $GroupSize $false $Frames
    }
    foreach ($group in 32, 64, 128, 256) {
        Run-Scale "1000000-group-$group" 1000000 720 $group $true $Frames
    }
} elseif ($Mode -eq 'prefix') {
    if (!$Prefix) { throw 'Prefix comparison requires -Prefix.' }
    foreach ($label in 'a1','b1','b2','a2') {
        Run-Scale "1000000-$label" 1000000 720 32 $true $Frames 1 $true $true
    }
} elseif ($Mode -eq 'memory') {
    foreach ($reuse in $false,$true,$true,$false) {
        $number++
        Run-Scale "memory-$number" 1000000 720 $GroupSize $true $Frames 1 $true $reuse
    }
} elseif ($Mode -eq 'overlap') {
    foreach ($slots in 1, 2, 3, 1) {
        $label = if ($slots -eq 1 -and $hasA) { '1-repeat' } else { "$slots" }
        Run-Scale "flight-$label" $StabilityTriangles 540 $GroupSize $true $Frames $slots $true $true
        if ($slots -eq 1) { $hasA = $true }
    }
} else {
    Run-Scale 'stability' $StabilityTriangles 540 $GroupSize $true $Frames $FramesInFlight $true $true
}
$results | Export-Csv -LiteralPath $summary -NoTypeInformation
$results
