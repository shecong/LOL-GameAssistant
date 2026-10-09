param([switch]$Rebuild, [switch]$Test)
$ErrorActionPreference = 'Stop'
$skinRepository = Split-Path $PSScriptRoot -Parent
$skinSource = Join-Path $skinRepository 'native\LeagueSkinCore'
$skinDestination = Join-Path $skinRepository 'LOL-GameAssistant\Tools\SkinCore'
if ($Rebuild -or -not (Test-Path -LiteralPath "$skinDestination\league-skin-core-host.exe")) {
    & "$skinSource\build.ps1"
    if ($LASTEXITCODE -ne 0) { throw 'Core build failed' }
    New-Item -ItemType Directory -Path "$skinDestination\licenses" -Force | Out-Null
    Copy-Item -LiteralPath "$skinSource\bin\league-skin-core-host.exe", "$skinSource\bin\league-skin-core.dll" -Destination $skinDestination
    Copy-Item -Path "$skinSource\licenses\*" -Destination "$skinDestination\licenses"
}
if ($Test) { & "$skinSource\build.ps1" -Test }
$skinHashes = @{}
foreach ($skinName in @('league-skin-core-host.exe','league-skin-core.dll')) {
    $skinHashes[$skinName] = (Get-FileHash -LiteralPath "$skinDestination\$skinName" -Algorithm SHA256).Hash.ToLowerInvariant()
}
$skinHashes | ConvertTo-Json | Set-Content -LiteralPath "$skinDestination\SHA256SUMS.json" -Encoding utf8
