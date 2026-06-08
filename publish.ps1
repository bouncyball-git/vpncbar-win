# Produce the release build: a framework-dependent single-file VpncBar.exe
# (~1.5 MB; needs the .NET 10 Desktop Runtime — the installer warns if absent)
# plus the engines folder beside it. Output: dist/app/.
param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot"
$proj = "$root\src\VpncBar.csproj"
$out = "$root\dist\app"

if (-not (Test-Path "$root\vendor\engines\bin\openconnect.exe")) { throw 'run fetch-openconnect.ps1 first' }
if (-not (Test-Path "$root\vendor\engines\bin\vpnc.exe")) { throw 'run build-vpnc.ps1 first' }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish $proj -c $Configuration -r win-x64 --self-contained false -o $out -p:_IsPublishing=true --nologo
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

# Lay the merged engines folder beside the single-file exe (publish doesn't
# carry the CopyToOutputDirectory subtree glob reliably, so do it explicitly —
# this is also the exact layout the installer ships).
$dest = "$out\engines"
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item "$root\vendor\engines\bin\*" $dest -Force

"published -> $out"
Get-ChildItem $out -Recurse -File | Measure-Object -Property Length -Sum |
    ForEach-Object { "  {0} files, {1:N1} MB total" -f $_.Count, ($_.Sum / 1MB) }
