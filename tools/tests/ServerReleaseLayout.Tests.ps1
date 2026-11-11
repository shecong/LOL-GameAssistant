#requires -Version 7.0
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path $PSScriptRoot -Parent) 'UpdatePublishing.ps1')
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('server-release-' + [Guid]::NewGuid().ToString('N'))
$assets = Join-Path $testRoot 'assets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null
function Assert-Fails([scriptblock]$Action) {
    $failed = $false
    try { & $Action | Out-Null } catch { $failed = $true }
    if (-not $failed) { throw 'Invalid release was accepted.' }
}
try {
    $version = '1.2.3'
    $runtimes = @('win-x64', 'win-x86', 'win-arm64')
    [IO.File]::WriteAllText((Join-Path $assets 'update.json'), (New-UpdateIndexJson -Version $version))
    foreach ($runtime in $runtimes) {
        $zipName = "LOL-GameAssistant-$runtime.zip"
        $zipPath = Join-Path $assets $zipName
        [IO.File]::WriteAllText($zipPath, "original-zip-bytes-$runtime")
        $manifest = @{ schemaVersion=1; version=$version; runtime=$runtime; package=$zipName;
            size=(Get-Item $zipPath).Length; sha256=(Get-FileHash $zipPath).Hash.ToLowerInvariant(); entryExecutable='sc助手.exe' }
        [IO.File]::WriteAllText((Join-Path $assets "update-$runtime.json"), ($manifest | ConvertTo-Json))
        $setupName = "sc-assistant-setup-$version-$runtime.exe"
        $setup = Join-Path $assets $setupName
        [IO.File]::WriteAllText($setup, "original-installer-bytes-$runtime")
        [IO.File]::WriteAllText("$setup.sha256", "$((Get-FileHash $setup).Hash.ToLowerInvariant())  $setupName`n")
    }
    $server = Export-UpdateServerLayout -AssetDirectory $assets
    foreach ($runtime in $runtimes) {
        $folder = Join-Path $server "v$version/$runtime"
        foreach ($name in @("LOL-GameAssistant-$runtime.zip", "sc-assistant-setup-$version-$runtime.exe",
            "sc-assistant-setup-$version-$runtime.exe.sha256")) {
            if ((Get-FileHash (Join-Path $folder $name)).Hash -ne (Get-FileHash (Join-Path $assets $name)).Hash) {
                throw "Export changed the original bytes: $name"
            }
        }
        if ((Get-FileHash (Join-Path $folder 'update.json')).Hash -ne
            (Get-FileHash (Join-Path $assets "update-$runtime.json")).Hash) { throw 'Manifest was not copied correctly.' }
    }
    if (@(Get-ChildItem $server -Recurse -File).Count -ne 13) { throw 'Incomplete server layout.' }
    if ((Get-FileHash (Join-Path $server 'update.json')).Hash -ne (Get-FileHash (Join-Path $assets 'update.json')).Hash) {
        throw 'Root index differs.'
    }
    Export-UpdateServerLayout -AssetDirectory $assets | Out-Null
    Write-Host 'PASS: all three architectures, installers, manifests and repeated export preserve original bytes.'

    $setup = Join-Path $assets "sc-assistant-setup-$version-win-arm64.exe"
    [IO.File]::WriteAllText($setup, 'tampered installer')
    $invalidOutput = Join-Path $testRoot 'invalid-output'
    Assert-Fails { Export-UpdateServerLayout -AssetDirectory $assets -OutputDirectory $invalidOutput }
    if (Test-Path $invalidOutput) { throw 'Failed validation created partial server output.' }
    $zipOnly = Export-UpdateServerLayout -AssetDirectory $assets -OutputDirectory (Join-Path $testRoot 'zip-only') -SkipInstaller
    if (@(Get-ChildItem $zipOnly -Recurse -File).Count -ne 7) { throw 'ZIP-only export contains unexpected files.' }
    [IO.File]::WriteAllText((Join-Path $assets 'LOL-GameAssistant-win-x64.zip'), 'tampered zip')
    Assert-Fails { Export-UpdateServerLayout -AssetDirectory $assets -OutputDirectory $invalidOutput -SkipInstaller }
    Assert-Fails { Export-UpdateServerLayout -AssetDirectory $assets -OutputDirectory $assets }
    Write-Host 'PASS: corrupted ZIP/installer and identical source/destination rejected; ZIP-only export supported.'
} finally {
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    $expectedPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedTestRoot.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedTestRoot) -notmatch '^server-release-[0-9a-f]{32}$') { throw 'Unexpected cleanup path.' }
    Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
}
