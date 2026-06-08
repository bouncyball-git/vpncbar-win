# Provision the MSYS2 build toolchain into vendor\msys64 — the from-scratch
# counterpart to "clean.ps1 -All" (which removes it). Idempotent: if the
# toolchain is already present it just ensures the build packages are installed
# (a quick no-op when they are).
#
#   .\setup-msys.ps1          provision vendor\msys64 if missing, ensure packages
#   .\setup-msys.ps1 -Force   wipe vendor\msys64 and reinstall from scratch
#
# This is what build-vpnc.ps1 / build-openconnect.ps1 expect at vendor\msys64
# (they fall back to a global C:\msys64 if you have one).
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$msys = "$root\vendor\msys64"
$bash = "$msys\usr\bin\bash.exe"

# The mingw64 toolchain the backend builds need.
$packages = @(
    'mingw-w64-x86_64-gcc', 'mingw-w64-x86_64-pkgconf',
    'mingw-w64-x86_64-gnutls', 'mingw-w64-x86_64-libgcrypt',
    'mingw-w64-x86_64-libxml2', 'mingw-w64-x86_64-zlib',
    'make', 'perl'
)

if ($Force -and (Test-Path $msys)) {
    "removing existing $msys ..."
    Remove-Item $msys -Recurse -Force
}

if (-not (Test-Path $bash)) {
    # Download + extract the MSYS2 base into vendor\ (the tarball's top-level
    # folder is "msys64", so it lands at vendor\msys64). 'latest' is a stable
    # URL; Windows' bundled tar (libarchive) handles .tar.xz.
    $url = 'https://repo.msys2.org/distrib/msys2-x86_64-latest.tar.xz'
    $tarxz = "$env:TEMP\msys2-x86_64-latest.tar.xz"
    "downloading MSYS2 base (~100 MB) ..."
    Invoke-WebRequest -Uri $url -OutFile $tarxz
    "extracting to $root\vendor ..."
    New-Item -ItemType Directory -Force "$root\vendor" | Out-Null
    & tar.exe -xf $tarxz -C "$root\vendor"
    if ($LASTEXITCODE -ne 0) { throw 'tar extraction failed (does your tar support .xz?)' }
    Remove-Item $tarxz -Force
    if (-not (Test-Path $bash)) { throw 'MSYS2 extraction did not produce vendor\msys64' }

    # First-run init (runs /etc/post-install), then update core — the runtime
    # update wants a fresh process, so update twice in separate invocations.
    "initializing MSYS2 ..."
    & $bash -lc 'exit 0'
    & $bash -lc 'pacman --noconfirm -Syuu'
    & $bash -lc 'pacman --noconfirm -Syuu'
}

"installing build packages ..."
$env:MSYSTEM = 'MINGW64'
& $bash -lc "pacman -S --needed --noconfirm $($packages -join ' ')"
if ($LASTEXITCODE -ne 0) { throw 'pacman install failed' }

"MSYS2 ready -> $msys"
& $bash -lc 'gcc --version | head -1'
