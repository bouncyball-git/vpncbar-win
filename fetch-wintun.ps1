# Fetch the signed Wintun release into vendor/wintun (dlls are gitignored;
# run this after a fresh clone). Version pinned; bump deliberately.
param([string]$Version = '0.14.1')

$w = "$PSScriptRoot\vendor\wintun"
$zip = "$w\wintun.zip"
New-Item -ItemType Directory -Force "$w\bin\amd64", "$w\bin\arm64" | Out-Null
Invoke-WebRequest -Uri "https://www.wintun.net/builds/wintun-$Version.zip" -OutFile $zip
Expand-Archive $zip -DestinationPath "$w\tmp" -Force
Copy-Item "$w\tmp\wintun\bin\amd64\wintun.dll" "$w\bin\amd64\"
Copy-Item "$w\tmp\wintun\bin\arm64\wintun.dll" "$w\bin\arm64\"
Copy-Item "$w\tmp\wintun\LICENSE.txt" "$w\LICENSE.txt"
Copy-Item "$w\tmp\wintun\include\wintun.h" "$w\wintun.h"
Remove-Item "$w\tmp", $zip -Recurse -Force
"wintun $Version fetched -> $w"
