# Build vpnc.exe from the vendored, Windows-ported source (MSYS2/mingw64) and
# collect it + its DLL closure straight into the shared dist/backend (gitignored).
# Both backends land in that one merged folder; the shared GnuTLS/etc DLL
# closure is not duplicated (ldd + cp -n dedups). Requires MSYS2 at vendor\msys64 (or a global C:\msys64):
#   pacman -S --needed mingw-w64-x86_64-{gcc,libgcrypt,gnutls,pkgconf} make perl
# and vendor/wintun/wintun.h present (fetch-wintun.ps1).

$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$rootMsys = '/' + "$root".Substring(0, 1).ToLower() + ("$root".Substring(2) -replace '\\', '/')   # G:\x -> /g/x (Windows PowerShell 5.1 + pwsh 7)
# Prefer the vendored MSYS2 (vendor\msys64); fall back to a global C:\msys64.
$bash = "$root\vendor\msys64\usr\bin\bash.exe"
if (-not (Test-Path $bash)) { $bash = 'C:\msys64\usr\bin\bash.exe' }
if (-not (Test-Path $bash)) { throw 'MSYS2 not found - run setup-msys.ps1 first' }
if (-not (Test-Path "$root\vendor\wintun\wintun.h")) { throw 'run fetch-wintun.ps1 first' }

New-Item -ItemType Directory -Force "$root\dist\backend" | Out-Null
Copy-Item "$root\vendor\wintun\bin\amd64\wintun.dll" "$root\dist\backend\" -Force

# Build vpnc.exe, then add it + its mingw DLL closure to the shared backend dir
# (ldd over everything already there converges the union across both backends).
$env:MSYSTEM = 'MINGW64'
& $bash -lc @"
set -e
cd $rootMsys/vendor/vpnc/src
make -f Makefile.win32 -j8
cp vpnc.exe $rootMsys/dist/backend/
cd $rootMsys/dist/backend
for i in 1 2 3; do
  for f in *.exe *.dll; do
    ldd "`$f" 2>/dev/null | awk '/mingw64/ {print `$3}'
  done | sort -u | while read d; do cp -n "`$d" . 2>/dev/null || true; done
done
ls -la
"@
if ($LASTEXITCODE -ne 0) { throw 'vpnc build failed' }
"done -> $root\dist\backend"
