param([string]$Executable, [string]$Out = (Join-Path $PSScriptRoot '../runs/translator'))
$ErrorActionPreference = 'Stop'
if (!$Executable) { $Executable = & (Join-Path $PSScriptRoot '../build.ps1') }
$Executable = (Resolve-Path $Executable).Path
$dir = (New-Item -Path $Out -ItemType Directory -Force).FullName
$packet = Join-Path $dir 'scene.vnm'
$writer = [IO.BinaryWriter]::new([IO.File]::Create($packet))
try {
    foreach ($v in 0x314d4e56,1,2,4,2) { $writer.Write([uint32]$v) }
    foreach ($v in 1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1) { $writer.Write([single]$v) }
    # Two meshlets share a vertex pool. Only the second is visible.
    foreach ($v in 0,4,0,1,0,0,4,1,1,1) { $writer.Write([uint32]$v) }
    foreach ($vertex in @(@(-0.8,-0.8),@(0.8,-0.8),@(0.8,0.8),@(-0.8,0.8))) {
        foreach ($v in @($vertex[0],$vertex[1],0.5,1,0.5,0.2,1,0,0,1)) { $writer.Write([single]$v) }
    }
    foreach ($v in 0,1,2,3,0,2,3,5) { $writer.Write([uint32]$v) }
} finally { $writer.Dispose() }

& $Executable --meshlet-packet $packet --meshlet-full-capacity --reuse-input-vertices --meshlet-group-size 32 `
    --frames-in-flight 3 --frames 12 --warmup 3 --width 128 --height 128 --out $dir | Out-Null
if ($LASTEXITCODE) { throw 'Packet consumer failed.' }
$report = Get-Content (Join-Path $dir 'scale.json') -Raw | ConvertFrom-Json
if (!$report.Passed -or $report.Frames.Count -ne 12 -or @($report.Frames | Where-Object VisibleTriangles -ne 1).Count) {
    throw 'External visibility, counters or reference mismatch.'
}
$original = [IO.File]::ReadAllBytes($packet)
foreach ($case in 'version','count','truncated','local-index','visibility','range','nan') {
    $bytes = [byte[]]$original.Clone()
    switch ($case) {
        version { $bytes[4] = 3 }
        count { $bytes[19] = 128 }
        truncated { $bytes = [byte[]]$bytes[0..($bytes.Length - 2)] }
        'local-index' { $bytes[$bytes.Length - 16] = 254 }
        visibility { $bytes[100] = 2 }
        range { $bytes[112] = 128 }
        nan { [BitConverter]::GetBytes([single]::NaN).CopyTo($bytes,20) }
    }
    $bad = Join-Path $dir "invalid-$case.vnm"
    [IO.File]::WriteAllBytes($bad,$bytes)
    $errors = & $Executable --meshlet-packet $bad --frames 1 --out $dir 2>&1 | Out-String
    if ($LASTEXITCODE -ne 1 -or $errors -notmatch 'InvalidDataException') { throw "Unexpected rejection for $case" }
}
$v2 = New-Object byte[] ($original.Length + 32)
[Array]::Copy($original,0,$v2,0,84)
$v2[4] = 2
# Explicit 1 x 2 x 1 dispatch and one indexed draw instance.
$v2[84] = 1; $v2[88] = 2; $v2[92] = 1; $v2[100] = 1
[Array]::Copy($original,84,$v2,116,$original.Length - 84)
$commandPacket = Join-Path $dir 'commands.vnm'
[IO.File]::WriteAllBytes($commandPacket,$v2)
& $Executable --meshlet-packet $commandPacket --frames 12 --warmup 3 --width 128 --height 128 --out "$dir/v2" | Out-Null
if ($LASTEXITCODE) { throw 'Explicit command packet failed.' }
$v2Report = Get-Content "$dir/v2/scale.json" -Raw | ConvertFrom-Json
if (!$v2Report.Passed -or $v2Report.PacketVersion -ne 2 -or @($v2Report.Frames | Where-Object VisibleTriangles -ne 1).Count) {
    throw 'Explicit command result mismatch.'
}
foreach ($offset in 84,88,92,96,100,104,108,112) {
    $bad = [byte[]]$v2.Clone()
    $bad[$offset] = 3
    $badPath = Join-Path $dir "invalid-command-$offset.vnm"
    [IO.File]::WriteAllBytes($badPath,$bad)
    $errors = & $Executable --meshlet-packet $badPath --frames 1 --out $dir 2>&1 | Out-String
    if ($LASTEXITCODE -ne 1 -or $errors -notmatch 'InvalidDataException') { throw "Unsafe command accepted at $offset" }
}
Write-Output 'Both packet versions passed 12 frames each and 15 malformed packets were rejected.'
