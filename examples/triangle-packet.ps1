param(
    [string]$Executable,
    [string]$Out = (Join-Path $PSScriptRoot '../runs/triangle-packet')
)
$ErrorActionPreference = 'Stop'
if (!$Executable) { $Executable = & (Join-Path $PSScriptRoot '../build.ps1') }
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$dir = (New-Item -Path $Out -ItemType Directory -Force).FullName
$packet = Join-Path $dir 'triangle.vnm'
$writer = [IO.BinaryWriter]::new([IO.File]::Create($packet))
try {
    # VNM1, version 2, one meshlet, three vertices, one primitive.
    foreach ($word in 0x314d4e56,2,1,3,1) { $writer.Write([uint32]$word) }
    foreach ($value in 1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1) { $writer.Write([single]$value) }
    # Dispatch (1,1,1), indexed draw template, then one visible meshlet.
    foreach ($word in 1,1,1,0,1,0,0,0,0,3,0,1,1) { $writer.Write([uint32]$word) }
    foreach ($vertex in @(@(-0.8,-0.7,1,0,0),@(0.8,-0.7,0,1,0),@(0,0.8,0,0,1))) {
        foreach ($value in @($vertex[0],$vertex[1],0.5,$vertex[2],$vertex[3],$vertex[4],1,0,0,1)) {
            $writer.Write([single]$value)
        }
    }
    foreach ($word in 0,1,2,7) { $writer.Write([uint32]$word) }
} finally { $writer.Dispose() }
& $Executable --meshlet-packet $packet --frames 1 --warmup 0 --width 128 --height 128 --out $dir
if ($LASTEXITCODE) { throw 'Packet rendering failed.' }
$report = Get-Content -LiteralPath (Join-Path $dir 'scale.json') -Raw | ConvertFrom-Json
if (!$report.Passed -or $report.PacketVersion -ne 2 -or $report.Frames[0].VisibleTriangles -ne 1) {
    throw 'Expected one visible triangle and exact reference images.'
}
