param(
    [string]$Executable,
    [string]$Out = (Join-Path $PSScriptRoot '../runs/meshlet-benchmarks'),
    [int]$Frames = 300,
    [int]$Warmup = 20,
    [int]$StabilityFrames = 2000
)

$ErrorActionPreference = 'Stop'
if (!$Executable) { $Executable = & (Join-Path $PSScriptRoot '../build.ps1') }
$Executable = (Resolve-Path -LiteralPath $Executable).Path

function Run-Meshlets([string]$Name, [int]$GroupSize, [int]$Count) {
    $folder = Join-Path $Out $Name
    & $Executable --meshlet-stability --width 64 --height 64 --frames $Count --warmup $Warmup `
        --meshlet-group-size $GroupSize --out $folder | Out-Null
    if ($LASTEXITCODE) { throw "Meshlet run failed for $Name." }
    $report = Get-Content -LiteralPath (Join-Path $folder 'triangle-meshlets.json') -Raw | ConvertFrom-Json
    if (!$report.Passed -or $report.Cases.Count -ne $Count -or $report.ThreadGroupSize -ne $GroupSize) {
        throw "Incomplete report for $Name."
    }
    $report.Cases | ForEach-Object {
        [pscustomobject]@{
            Frame = [int]($_.Name -replace '^stability-', '')
            GpuComputeMs = $_.Gpu.GpuComputeMs
            GpuRasterMs = $_.Gpu.GpuRasterMs
            RequestedTriangles = $_.Gpu.RequestedTriangles
            AcceptedTriangles = $_.Gpu.AcceptedTriangles
            WrittenTriangles = $_.Gpu.WrittenTriangles
            WrittenVertices = $_.Gpu.WrittenVertices
            Drops = $_.Gpu.Dropped
            ImagesMatch = $_.ImagesMatch
        }
    } | Export-Csv -LiteralPath (Join-Path $folder 'frames.csv') -NoTypeInformation
    Write-Output "$Name completed $Count measured frames on $($report.Hardware.Adapter)."
}

Run-Meshlets a1 1 $Frames
Run-Meshlets b32 32 $Frames
Run-Meshlets b64 64 $Frames
Run-Meshlets a2 1 $Frames
Run-Meshlets stability 1 $StabilityFrames
