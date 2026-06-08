# Build the Windows installer: publish the app, then run Inno Setup over
# dist/setup/VpncBar.iss. Output: dist/setup/VpncBar-<version>-setup.exe.
# Requires Inno Setup 6 (winget install JRSoftware.InnoSetup).

$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot"

if (-not (Test-Path "$root\dist\backend\vpnc.exe")) { throw 'run build-openconnect.ps1 + build-vpnc.ps1 first (populate dist\backend)' }

& "$PSScriptRoot\publish-app.ps1"

$iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    # machine-wide and user-scope (winget installs user-scope by default) paths
    foreach ($p in "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe",
                   "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
                   "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") {
        if (Test-Path $p) { $iscc = $p; break }
    }
}
if (-not $iscc) { throw 'Inno Setup not found. Install it: winget install JRSoftware.InnoSetup' }

& $iscc "$root\dist\setup\VpncBar.iss"
if ($LASTEXITCODE -ne 0) { throw 'iscc failed' }
"installer -> $root\dist\setup"
Get-ChildItem "$root\dist\setup\*-setup.exe" | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}
