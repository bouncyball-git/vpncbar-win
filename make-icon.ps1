# Regenerate src/assets/VpncBar.ico from the SVG art. The rendering
# lives in the app itself (TrayIcons mini-SVG renderer), so the icon always
# matches what the tray shows: build, then ask the exe to write the ico.
param([string]$Out = "$PSScriptRoot\src\assets\VpncBar.ico")

$proj = "$PSScriptRoot\src\VpncBar.csproj"
dotnet build $proj -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'build failed' }
& "$PSScriptRoot\src\bin\Debug\net10.0-windows\VpncBar.exe" --make-icon $Out
