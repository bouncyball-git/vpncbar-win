# Uninstall VpncBar: stop the tray, stop + deregister the Windows service, and
# delete the installed program files (a Program Files\VpncBar copy + Start Menu
# shortcut, if a real install left them). The dev build in src\bin and the
# session logs in %ProgramData%\VpncBar are left untouched.
#
# You're asked SEPARATELY whether to also remove your saved VPN profiles and
# your stored credentials — both are kept by default.
#
#   .\scripts\uninstall-dev.ps1

$ErrorActionPreference = 'Stop'

# Stop the tray (the demand-start service self-stops with it).
Get-Process VpncBar -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

# Elevated: deregister the service (by name, so binPath location is irrelevant)
# and remove an installed Program Files copy + its Start Menu shortcut.
$elevated = @'
sc.exe stop VpncBar 2>$null | Out-Null
sc.exe delete VpncBar 2>$null | Out-Null
Remove-Item "$env:ProgramFiles\VpncBar" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\VpncBar" -Recurse -Force -ErrorAction SilentlyContinue
'@
Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile', '-Command', $elevated
Write-Host 'Service deregistered; installed files removed.' -ForegroundColor Green

# Optional — saved profiles (%APPDATA%\vpncbar; per-user, no elevation needed).
if ((Read-Host 'Also remove your saved VPN profiles? (y/N)') -match '^(y|yes)$') {
    Remove-Item "$env:APPDATA\vpncbar" -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host '  profiles removed.' -ForegroundColor Yellow
} else {
    Write-Host '  profiles kept.'
}

# Optional — stored credentials (Credential Manager; per-user). Items are named
# vpnc-<uuid>-secret / vpnc-<uuid>-password.
if ((Read-Host 'Also remove saved passwords/secrets from Credential Manager? (y/N)') -match '^(y|yes)$') {
    $n = 0
    foreach ($line in (cmdkey /list)) {
        if ($line -match '(vpnc-[\w-]+-(?:secret|password))') {
            cmdkey /delete:$($Matches[1]) | Out-Null
            $n++
        }
    }
    Write-Host "  removed $n credential(s)." -ForegroundColor Yellow
} else {
    Write-Host '  credentials kept.'
}

Write-Host "`nDone. Session logs (%ProgramData%\VpncBar) and the build (src\bin) were left untouched." -ForegroundColor Green
