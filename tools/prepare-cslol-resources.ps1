param([string]$SourceResources = 'C:\app\PLskin\resources')
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\LOL-GameAssistant'
$tools = Join-Path $project 'Tools\Cslol'
$skins = Join-Path $project 'Data\Skins'
$toolNames = @('mod-tools.exe', 'cslol-dll.dll', 'hashes.game.txt', 'ritobin_cli.exe', 'wad-extract.exe', 'wad-make.exe')
$sourceFiles = @($toolNames | ForEach-Object { Join-Path $SourceResources "cslol-tools\$_" })
$sourceFiles += Join-Path $SourceResources 'GameSkins\manifest.json'
$hasLibrary = Test-Path -LiteralPath (Join-Path $skins 'skinlib') -PathType Container
if (-not $hasLibrary) { $sourceFiles += Join-Path $SourceResources 'GameSkins\skins.plskin' }
foreach ($path in $sourceFiles) { if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing resource: $path" } }
New-Item -ItemType Directory -Path $tools, $skins -Force | Out-Null
foreach ($name in $toolNames) { Copy-Item -LiteralPath (Join-Path $SourceResources "cslol-tools\$name") -Destination (Join-Path $tools $name) }
Copy-Item -LiteralPath (Join-Path $SourceResources 'GameSkins\manifest.json') -Destination (Join-Path $skins 'manifest.json')
if (-not $hasLibrary) { Copy-Item -LiteralPath (Join-Path $SourceResources 'GameSkins\skins.plskin') -Destination (Join-Path $skins 'skins.plskin') }
$receipt = foreach ($name in $toolNames) { $path = Join-Path $tools $name; [ordered]@{ file=$name; sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash } }
$receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $tools 'SHA256SUMS.json') -Encoding utf8
Write-Output "File-skin resources prepared in $project. They are copied by the project build/publish rules and excluded from Git."
