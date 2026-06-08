# Remove build artifacts.
#
#   .\clean.ps1        Everyday clean — the C# build output, the published app,
#                      and the built installer. Keeps the compiled backends
#                      (dist\backend) since they take minutes to rebuild.
#                      Never touches tracked source.
#
#   .\clean.ps1 -All   Full reset to a from-scratch state — also removes the
#                      compiled backends, the vendored MSYS2 toolchain, and the
#                      fetched/built backend sources (openconnect + wintun). The
#                      next build re-provisions, re-fetches, and recompiles
#                      everything. (Git still has any tracked provenance files;
#                      the fetch scripts recreate them either way.)
param([switch]$All)

$root = $PSScriptRoot

function Nuke($path) {
    if (Test-Path $path) { Remove-Item $path -Recurse -Force -ErrorAction SilentlyContinue; "  - $path" }
}

# Free the C# build output: kill the tray (the demand-start service self-stops
# with it, releasing its lock on the exe).
Get-Process VpncBar -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
for ($i = 0; $i -lt 10 -and (Get-Service VpncBar -ErrorAction SilentlyContinue).Status -eq 'Running'; $i++) {
    Start-Sleep -Milliseconds 300
}

"cleaning build output:"
Nuke "$root\src\bin"
Nuke "$root\src\obj"
Nuke "$root\dist\app"
# the built installer only — dist\setup\VpncBar.iss is tracked source
Get-ChildItem "$root\dist\setup\*-setup.exe" -ErrorAction SilentlyContinue | ForEach-Object {
    Remove-Item $_.FullName -Force; "  - $($_.FullName)"
}

if ($All) {
    "full reset (backends, fetched sources, toolchain):"
    Nuke "$root\dist\backend"             # compiled backends (build-vpnc + build-openconnect)
    Nuke "$root\vendor\msys64"            # vendored MSYS2 toolchain (setup-msys re-provisions)
    Nuke "$root\vendor\openconnect"       # source tarball + build tree (build-openconnect re-fetches/builds)
    Nuke "$root\vendor\wintun"            # wintun dll + header (fetch-wintun re-fetches)
    # vpnc builds in-place in its source dir — drop its objects/exe (the source stays)
    Get-ChildItem "$root\vendor\vpnc\src" -Include *.o, *.exe, *.dll, vpnc-debug.* -Recurse -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Item $_.FullName -Force }
    "  - vendor\vpnc\src build objects"
}

"done."
