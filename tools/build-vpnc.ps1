# Build vpnc.exe from the vendored, Windows-ported source (MSYS2/mingw64) and
# collect it + its DLL closure into vendor/vpnc/bin (gitignored). CI mirrors
# this. Requires MSYS2 at C:\msys64 with:
#   pacman -S --needed mingw-w64-x86_64-{gcc,libgcrypt,gnutls,pkgconf} make perl
# and vendor/wintun/wintun.h present (tools/fetch-wintun.ps1).

$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$rootMsys = ($root -replace '\\', '/' -replace '^([A-Za-z]):', { '/' + $_.Groups[1].Value.ToLower() })
$bash = 'C:\msys64\usr\bin\bash.exe'
if (-not (Test-Path $bash)) { throw 'MSYS2 not found at C:\msys64' }
if (-not (Test-Path "$root\vendor\wintun\wintun.h")) { throw 'run tools/fetch-wintun.ps1 first' }

$env:MSYSTEM = 'MINGW64'
& $bash -lc @"
set -e
cd $rootMsys/vendor/vpnc/src
make -f Makefile.win32 -j8
cd $rootMsys/vendor/vpnc
mkdir -p bin && rm -f bin/*
cp src/vpnc.exe bin/
for i in 1 2 3; do
  for f in bin/*.exe bin/*.dll; do
    ldd "`$f" 2>/dev/null | awk '/mingw64/ {print `$3}'
  done | sort -u | while read d; do cp -n "`$d" bin/ 2>/dev/null || true; done
done
ls -la bin/
"@
if ($LASTEXITCODE -ne 0) { throw 'vpnc build failed' }
"done -> $root\vendor\vpnc\bin"
