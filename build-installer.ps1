# Build the Windows installer: publish the app, then run Inno Setup over
# installer/VpncBar.iss. Output: dist/VpncBar-<version>-setup.exe.
# Requires Inno Setup 6 (winget install JRSoftware.InnoSetup).

$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot"

& "$PSScriptRoot\publish.ps1"

$iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    foreach ($p in 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe', 'C:\Program Files\Inno Setup 6\ISCC.exe') {
        if (Test-Path $p) { $iscc = $p; break }
    }
}
if (-not $iscc) { throw 'Inno Setup not found. Install it: winget install JRSoftware.InnoSetup' }

& $iscc "$root\installer\VpncBar.iss"
if ($LASTEXITCODE -ne 0) { throw 'iscc failed' }
"installer -> $root\dist"
Get-ChildItem "$root\dist\*-setup.exe" | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}
