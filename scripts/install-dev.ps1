# Dev install: register the VpncBar service against the LOCAL build and launch
# the tray — so you can run/test the full app (tray + privileged service)
# straight from src\bin without building the Inno installer. This is the
# from-source counterpart to running the real installer.
#
# The service registration needs admin, so that one step self-elevates (UAC
# prompt); the tray runs unprivileged as you. --install-service is idempotent,
# so re-running just re-points the service at the latest build.
#
#   .\scripts\install-dev.ps1              build (Debug), register service, launch tray
#   .\scripts\install-dev.ps1 -Release     use the Release build instead of Debug
#
# To remove it, use uninstall-dev.ps1.
param([switch]$Release)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$config = if ($Release) { 'Release' } else { 'Debug' }
$exe = "$root\src\bin\$config\net10.0-windows\VpncBar.exe"

# 1. Build the dev app (also stops the tray, freeing the exe).
if ($Release) { & "$PSScriptRoot\build-app.ps1" } else { & "$PSScriptRoot\build-app.ps1" -Debug }
if (-not (Test-Path $exe)) { throw "build produced no exe at $exe" }

# 2. Register the service at this exe (elevated — UAC). Idempotent: reconfigures
#    binPath/start/SDDL if the service already exists.
$p = Start-Process $exe '--install-service' -Verb RunAs -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "service registration failed (exit $($p.ExitCode))" }

# 3. Launch the tray (unprivileged) — it starts the demand-start service.
Start-Process $exe
Start-Sleep -Seconds 2
$status = (Get-Service VpncBar -ErrorAction SilentlyContinue).Status
Write-Host "`nVpncBar ($config) installed and running." -ForegroundColor Green
Write-Host "  service -> $exe --service   [$status]"
Write-Host "  tray    -> $exe"
