# Compile with the x64 .NET Framework compiler supplied by Windows.
param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'build'))

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:windir 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw "The x64 .NET Framework 4.x compiler was not found: $compiler"
}
$build = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
New-Item -ItemType Directory -Path $build -Force | Out-Null
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File |
    Sort-Object Name | Select-Object -ExpandProperty FullName
& $compiler /nologo /target:exe /platform:x64 /unsafe+ /optimize+ /warnaserror+ /codepage:65001 `
    /reference:System.Drawing.dll,System.Windows.Forms.dll,System.Web.Extensions.dll `
    "/out:$build/Navi10.exe" $sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'shaders') -Filter '*.hlsl' -File | Copy-Item -Destination $build
Write-Output (Join-Path $build 'Navi10.exe')
