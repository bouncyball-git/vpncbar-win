# Build everything from scratch and produce the finished installer.
#
# Runs the full pipeline A→Z:
#   1. setup-msys        provision the MSYS2/mingw toolchain   (vendor\msys64)
#   2. fetch-wintun      download the signed wintun.dll        (vendor\wintun)
#   3. build-openconnect compile openconnect                   (dist\backend)
#   4. build-vpnc        compile vpnc                          (dist\backend)
#   5. build-installer   publish the app + run Inno Setup      (dist\setup)
#
# Result: dist\setup\VpncBar-<version>-setup.exe
#
#   .\build-all.ps1          build everything (reuses an existing toolchain)
#   .\build-all.ps1 -Force   also reprovision the toolchain from scratch
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$started = Get-Date

function Step($n, $name) { Write-Host "`n=== [$n/5] $name $('=' * [Math]::Max(0, 48 - $name.Length))" -ForegroundColor Cyan }

# --- Fail fast on missing prerequisites (before the long backend builds) ---
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET 10 SDK not found. Install it: winget install Microsoft.DotNet.SDK.10'
}

function Resolve-Iscc {
    $c = (Get-Command iscc -ErrorAction SilentlyContinue).Source
    if ($c) { return $c }
    # machine-wide and user-scope (winget installs user-scope by default) paths
    foreach ($p in "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe",
                   "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
                   "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") {
        if (Test-Path $p) { return $p }
    }
    return $null
}
if (-not (Resolve-Iscc)) {
    Write-Host 'Inno Setup not found — installing via winget...' -ForegroundColor Yellow
    winget install --silent --accept-source-agreements --accept-package-agreements JRSoftware.InnoSetup
    if (-not (Resolve-Iscc)) {
        throw 'Inno Setup still not found after winget. Install it manually: winget install JRSoftware.InnoSetup'
    }
}

# --- Pipeline ---
Step 1 'Provision MSYS2 toolchain'
if ($Force) { & "$root\setup-msys.ps1" -Force } else { & "$root\setup-msys.ps1" }

Step 2 'Fetch Wintun'
& "$root\fetch-wintun.ps1"

Step 3 'Build openconnect'
& "$root\build-openconnect.ps1"

Step 4 'Build vpnc'
& "$root\build-vpnc.ps1"

Step 5 'Publish app + build installer'
& "$root\build-installer.ps1"

# --- Report ---
$setup = Get-ChildItem "$root\dist\setup\*-setup.exe" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $setup) { throw 'pipeline finished but no installer was produced in dist\setup' }
$mins = [Math]::Round(((Get-Date) - $started).TotalMinutes, 1)
Write-Host "`n=== DONE in $mins min ===" -ForegroundColor Green
Write-Host ("installer -> {0}  ({1:N1} MB)" -f $setup.FullName, ($setup.Length / 1MB))
