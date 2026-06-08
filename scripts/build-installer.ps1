# Build the Windows installer: publish the app, then run Inno Setup over
# dist/setup/VpncBar.iss. Output: dist/setup/VpncBar-<version>-setup.exe.
# Inno Setup 6 is auto-installed via winget if it isn't already present.

$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."

# Locate ISCC.exe (PATH, machine-wide, or the user-scope dir winget uses).
function Resolve-Iscc {
    $c = (Get-Command iscc -ErrorAction SilentlyContinue).Source
    if ($c) { return $c }
    foreach ($p in "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe",
                   "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
                   "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") {
        if (Test-Path $p) { return $p }
    }
    return $null
}

if (-not (Test-Path "$root\dist\backend\vpnc.exe")) { throw 'run build-openconnect.ps1 + build-vpnc.ps1 first (populate dist\backend)' }

# Auto-install Inno Setup if missing.
$iscc = Resolve-Iscc
if (-not $iscc) {
    Write-Host 'Inno Setup not found - installing via winget...' -ForegroundColor Yellow
    winget install --silent --accept-source-agreements --accept-package-agreements JRSoftware.InnoSetup
    $iscc = Resolve-Iscc
    if (-not $iscc) { throw 'Inno Setup still not found after winget. Install it manually: winget install JRSoftware.InnoSetup' }
}

& "$PSScriptRoot\publish-app.ps1"

& $iscc "$root\dist\setup\VpncBar.iss"
if ($LASTEXITCODE -ne 0) { throw 'iscc failed' }
"installer -> $root\dist\setup"
Get-ChildItem "$root\dist\setup\*-setup.exe" | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}
