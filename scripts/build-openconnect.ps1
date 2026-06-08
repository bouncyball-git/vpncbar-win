# Build openconnect from source (MSYS2/mingw64) and collect its binaries
# straight into the shared dist/backend (gitignored), where vpnc lands too.
# Requires MSYS2 at vendor\msys64 (or a global C:\msys64) with:
#   pacman -S --needed mingw-w64-x86_64-gcc mingw-w64-x86_64-pkgconf \
#       mingw-w64-x86_64-gnutls mingw-w64-x86_64-libxml2 mingw-w64-x86_64-zlib make
param([string]$Version = '9.12')

$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$oc = "$root\vendor\openconnect"
$eng = "$root\dist\backend"
$tarball = "$oc\src\openconnect-$Version.tar.gz"
# Prefer the vendored MSYS2 (vendor\msys64); fall back to a global C:\msys64.
$msys = "$root\vendor\msys64"
if (-not (Test-Path "$msys\usr\bin\bash.exe")) { $msys = 'C:\msys64' }
$bash = "$msys\usr\bin\bash.exe"
if (-not (Test-Path $bash)) { throw 'MSYS2 not found - run setup-msys.ps1 first' }

New-Item -ItemType Directory -Force "$oc\src", $eng | Out-Null
Copy-Item "$root\vendor\wintun\bin\amd64\wintun.dll" $eng -Force
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

# Collect openconnect.exe (+ libopenconnect dll) and the mingw64 DLL closure
# straight into the shared backend dir (ldd over everything there converges
# the union across both backends).
& $bash -lc @"
set -e
cd $rootMsys/dist/backend
cp $rootMsys/vendor/openconnect/build/openconnect-$Version/.libs/openconnect.exe . 2>/dev/null || cp $rootMsys/vendor/openconnect/build/openconnect-$Version/openconnect.exe .
cp $rootMsys/vendor/openconnect/build/openconnect-$Version/.libs/*.dll . 2>/dev/null || true
for i in 1 2 3; do   # closure is transitive; a few passes converge
  for f in *.exe *.dll; do
    ldd "`$f" 2>/dev/null | awk '/mingw64/ {print `$3}'
  done | sort -u | while read d; do cp -n "`$d" . 2>/dev/null || true; done
done
ls -la
"@
if ($LASTEXITCODE -ne 0) { throw "collecting binaries failed" }

# Provenance for the LGPL source offer + NOTICE.
$pkgs = & "$msys\usr\bin\pacman.exe" -Q mingw-w64-x86_64-gnutls mingw-w64-x86_64-libxml2 mingw-w64-x86_64-zlib mingw-w64-x86_64-gcc 2>$null
@"
openconnect $Version — built from src/openconnect-$Version.tar.gz
(https://www.infradead.org/openconnect/download/) with MSYS2 mingw64.
Dependency packages at build time:
$($pkgs -join "`n")
"@ | Set-Content "$oc\VERSIONS.txt"
"done -> $eng"
