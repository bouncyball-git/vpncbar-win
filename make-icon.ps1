# Regenerate src/assets/VpncBar.ico from the SVG art AND rebuild the app with
# the new icon embedded — run this after editing vpn-off.svg.
#
# The icon rendering lives in the app itself (TrayIcons mini-SVG renderer), so
# the file/taskbar icon always matches what the tray draws. Two builds are
# unavoidable: the first produces an exe that renders the .ico from the current
# SVGs; the second bakes that .ico into the exe (it's embedded at build time
# via <ApplicationIcon>). Both go through build-app.ps1, which frees the locked
# exe by stopping the tray first.
param([string]$Out = "$PSScriptRoot\src\assets\VpncBar.ico")

$ErrorActionPreference = 'Stop'

# 1. Build so the exe reflects the current embedded SVG art.
& "$PSScriptRoot\build-app.ps1" -Debug

# 2. Render the multi-size .ico from those SVGs.
& "$PSScriptRoot\src\bin\Debug\net10.0-windows\VpncBar.exe" --make-icon $Out

# 3. Rebuild so the freshly written .ico is embedded as the exe/taskbar icon.
& "$PSScriptRoot\build-app.ps1" -Debug

"icon regenerated and embedded -> $Out"
