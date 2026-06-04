# Build openconnect from source (MSYS2/mingw64) and collect the binaries into
# vendor/openconnect/bin (gitignored). This is the canonical recipe — the CI
# workflow mirrors it. Requires MSYS2 at C:\msys64 with:
#   pacman -S --needed mingw-w64-x86_64-gcc mingw-w64-x86_64-pkgconf \
#       mingw-w64-x86_64-gnutls mingw-w64-x86_64-libxml2 mingw-w64-x86_64-zlib make
param([string]$Version = '9.12')

$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$oc = "$root\vendor\openconnect"
$tarball = "$oc\src\openconnect-$Version.tar.gz"
$bash = 'C:\msys64\usr\bin\bash.exe'
if (-not (Test-Path $bash)) { throw 'MSYS2 not found at C:\msys64' }

New-Item -ItemType Directory -Force "$oc\src", "$oc\bin" | Out-Null
if (-not (Test-Path $tarball)) {
    Invoke-WebRequest -Uri "https://www.infradead.org/openconnect/download/openconnect-$Version.tar.gz" -OutFile $tarball
}

$rootMsys = ($root -replace '\\', '/' -replace '^([A-Za-z]):', { '/' + $_.Groups[1].Value.ToLower() })
$env:MSYSTEM = 'MINGW64'

# Build. Notes baked into the flags:
#  - wintun.h comes from vendor/wintun (the openconnect tarball doesn't ship it)
#  - gcc 14 made incompatible-pointer-types an error; 9.12 trips it on Windows
#  - vpnc-script path is irrelevant (we always pass --script), but configure wants one
& $bash -lc @"
set -e
cd $rootMsys/vendor/openconnect
rm -rf build && mkdir -p build
tar xzf src/openconnect-$Version.tar.gz -C build
cp ../wintun/wintun.h build/openconnect-$Version/
cd build/openconnect-$Version
./configure --with-vpnc-script=vpncbar-script --with-gnutls --disable-nls
make -j8 CFLAGS='-O2 -Wno-incompatible-pointer-types'
"@
if ($LASTEXITCODE -ne 0) { throw "openconnect build failed" }

# Collect openconnect.exe (+ libopenconnect dll) and the mingw64 DLL closure.
& $bash -lc @"
set -e
cd $rootMsys/vendor/openconnect
rm -f bin/*
cp build/openconnect-$Version/.libs/openconnect.exe bin/ 2>/dev/null || cp build/openconnect-$Version/openconnect.exe bin/
cp build/openconnect-$Version/.libs/*.dll bin/ 2>/dev/null || true
for i in 1 2 3; do   # closure is transitive; a few passes converge
  for f in bin/*.exe bin/*.dll; do
    ldd "`$f" 2>/dev/null | awk '/mingw64/ {print `$3}'
  done | sort -u | while read d; do cp -n "`$d" bin/ 2>/dev/null || true; done
done
ls -la bin/
"@
if ($LASTEXITCODE -ne 0) { throw "collecting binaries failed" }

# Provenance for the LGPL source offer + NOTICE.
$pkgs = & C:\msys64\usr\bin\pacman.exe -Q mingw-w64-x86_64-gnutls mingw-w64-x86_64-libxml2 mingw-w64-x86_64-zlib mingw-w64-x86_64-gcc 2>$null
@"
openconnect $Version — built from src/openconnect-$Version.tar.gz
(https://www.infradead.org/openconnect/download/) with MSYS2 mingw64.
Dependency packages at build time:
$($pkgs -join "`n")
"@ | Set-Content "$oc\VERSIONS.txt"
"done -> $oc\bin"
