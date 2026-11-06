param([Parameter(Mandatory = $true)][string]$Source)
$ErrorActionPreference = 'Stop'
$expectedHash = 'E4FBBF9671303712A764D505D9E9A6C5FAE7004873899255AB798F759D23E69B'
if ((Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash -ne $expectedHash) { throw 'HID.dll SHA256 does not match the pinned user-supplied loader.' }
$componentDir = Join-Path (Split-Path $PSScriptRoot -Parent) 'LOL-GameAssistant\Tools\ResourceSkins'
New-Item -ItemType Directory -Path $componentDir -Force | Out-Null
Copy-Item -LiteralPath $Source -Destination (Join-Path $componentDir 'HID.dll') -Force
Write-Output "Prepared verified resource loader: $componentDir"
