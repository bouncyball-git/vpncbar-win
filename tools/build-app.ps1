# Build the VpncBar .NET app (framework-dependent, for local run/test).
#   default        Release, optimized, no debug symbols (stripped)
#   -Debug         Debug, with symbols (.pdb) — for stepping in a debugger
#
# This builds only the C# app; the bundled backends (engines/) are produced by
# fetch-openconnect.ps1 + build-vpnc.ps1 and copied into the output by the
# CopyEngines target. For the self-contained single-file release, use publish.ps1.
param([switch]$Debug)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$proj = "$root\src\VpncBar.csproj"
$config = if ($Debug) { 'Debug' } else { 'Release' }

# Free the exe: kill the tray (same user); the demand-start service then
# self-stops (owner watch), releasing its lock on the binary.
Get-Process VpncBar -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
for ($i = 0; $i -lt 10 -and (Get-Service VpncBar -ErrorAction SilentlyContinue).Status -ne 'Stopped'; $i++) {
    Start-Sleep -Milliseconds 300
}

# Release is stripped (no .pdb); Debug keeps full symbols.
$args = @('build', $proj, '-c', $config, '--nologo')
if (-not $Debug) { $args += @('-p:DebugType=none', '-p:DebugSymbols=false') }

dotnet @args
if ($LASTEXITCODE -ne 0) { throw "$config build failed" }

$out = "$root\src\bin\$config\net10.0-windows"
"built $config -> $out\VpncBar.exe"
if (-not $Debug -and (Test-Path "$out\VpncBar.pdb")) { Remove-Item "$out\VpncBar.pdb" -Force }
