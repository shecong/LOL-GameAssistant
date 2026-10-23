param(
    [Parameter(Mandatory)][ValidateSet('Classic', 'Hybrid', 'Relay')][string]$Project,
    [ValidateSet('Build', 'Clean')][string]$Action = 'Build',
    [string]$WdkRoot = '', [string]$SdkVersion = '10.0.26100.0'
)
$ErrorActionPreference = 'Stop'
# VS/MSBuild may inherit a PowerShell 7-only PSModulePath. Restore the modules
# belonging to the Windows PowerShell process used by the Makefile command.
$env:PSModulePath = "$PSHOME\Modules;$env:PSModulePath"
Import-Module Microsoft.PowerShell.Utility -ErrorAction Stop
$skinProjectFolder = @{ Classic = 'LeagueSkinCore'; Hybrid = 'LeagueSkinHybrid'; Relay = 'SkinCoreRelay' }[$Project]
$skinProjectRoot = Join-Path $PSScriptRoot $skinProjectFolder
if ($Action -eq 'Clean') {
    # Remove only named build outputs, never the runtime copies or original backup.
    $skinOutputNames = switch ($Project) {
        'Classic' { @('league-skin-core.dll', 'league-skin-core-host.exe', 'league-skin-core.lib', 'league-skin-core.exp', 'core-tests.exe') }
        'Hybrid' { @('league-skin-core.dll', 'league-skin-hybrid-host.exe', 'league-skin-core.lib', 'league-skin-core.exp', 'relay-test-client.exe', 'relay-dispatch-tests.exe') }
        'Relay' { @('SkinCoreRelay.sys', 'driver.obj') }
    }
    foreach ($skinOutputName in $skinOutputNames) {
        $skinOutputPath = Join-Path (Join-Path $skinProjectRoot 'bin') $skinOutputName
        if (Test-Path -LiteralPath $skinOutputPath -PathType Leaf) { Remove-Item -LiteralPath $skinOutputPath -Force }
    }
    return
}
if ($Project -eq 'Relay') { & "$skinProjectRoot\build.ps1" -WdkRoot $WdkRoot -SdkVersion $SdkVersion }
else { & "$skinProjectRoot\build.ps1" }
if ($Project -eq 'Classic') {
    $skinClassicRuntime = Join-Path $PSScriptRoot '..\LOL-GameAssistant\Tools\SkinCore'
    New-Item -ItemType Directory -Path $skinClassicRuntime -Force | Out-Null
    Copy-Item -LiteralPath "$skinProjectRoot\bin\league-skin-core.dll", "$skinProjectRoot\bin\league-skin-core-host.exe" -Destination $skinClassicRuntime -Force
    # Retain the established manifest schema used by the original core package.
    $skinClassicHashes = [ordered]@{}
    foreach ($skinFileName in @('league-skin-core-host.exe', 'league-skin-core.dll')) {
        $skinClassicHashes[$skinFileName] = (Get-FileHash -LiteralPath (Join-Path $skinClassicRuntime $skinFileName) -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $skinClassicHashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $skinClassicRuntime 'SHA256SUMS.json') -Encoding utf8
}
