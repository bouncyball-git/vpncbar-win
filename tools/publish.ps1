# Produce the release build: a self-contained single-file VpncBar.exe (bundles
# the .NET runtime — end users need nothing installed) plus the backend folders
# beside it. Output: dist/app/.
param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$proj = "$root\src\VpncBar\VpncBar.csproj"
$out = "$root\dist\app"

if (-not (Test-Path "$root\vendor\openconnect\bin\openconnect.exe")) { throw 'run tools/fetch-openconnect.ps1 first' }
if (-not (Test-Path "$root\vendor\vpnc\bin\vpnc.exe")) { throw 'run tools/build-vpnc.ps1 first' }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish $proj -c $Configuration -o $out -p:_IsPublishing=true --nologo
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

# Lay the bundled backends beside the single-file exe (publish doesn't carry
# the CopyToOutputDirectory subtree globs reliably, so do it explicitly — this
# is also the exact layout the installer ships).
foreach ($be in 'openconnect', 'vpnc') {
    $dest = "$out\$be"
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item "$root\vendor\$be\bin\*" $dest -Force
    Copy-Item "$root\vendor\wintun\bin\amd64\wintun.dll" $dest -Force
}

"published -> $out"
Get-ChildItem $out -Recurse -File | Measure-Object -Property Length -Sum |
    ForEach-Object { "  {0} files, {1:N1} MB total" -f $_.Count, ($_.Sum / 1MB) }
