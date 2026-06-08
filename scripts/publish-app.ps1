# Produce the release build: a framework-dependent single-file VpncBar.exe
# (~0.4 MB; needs the .NET 10 Desktop Runtime — the installer warns if absent).
# Output: dist/app/. The backends live separately in dist/backend/ (produced by
# build-openconnect.ps1 + build-vpnc.ps1); the installer ships app + backend
# side by side.
param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$proj = "$root\src\VpncBar.csproj"
$out = "$root\dist\app"

if (-not (Test-Path "$root\dist\backend\openconnect.exe")) { throw 'run build-openconnect.ps1 first' }
if (-not (Test-Path "$root\dist\backend\vpnc.exe")) { throw 'run build-vpnc.ps1 first' }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish $proj -c $Configuration -r win-x64 --self-contained false -o $out -p:_IsPublishing=true --nologo
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

"published -> $out"
Get-ChildItem $out -Recurse -File | Measure-Object -Property Length -Sum |
    ForEach-Object { "  {0} files, {1:N1} MB total" -f $_.Count, ($_.Sum / 1MB) }
