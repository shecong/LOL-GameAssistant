#requires -Version 7.0
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path $PSScriptRoot -Parent) 'UpdatePublishing.ps1')
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$entry = Get-UpdateEntryExecutable -Repository $repository
if ($entry -cne 'sc助手.exe') { throw 'Incorrect publish entry.' }

$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ('update-publishing-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDirectory | Out-Null
function Assert-Rejected {
    param([scriptblock]$Action, [string]$Message)
    $rejected = $false
    try { & $Action } catch {
        if (-not $_.Exception.Message.Contains($Message)) { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw "Expected rejection: $Message" }
}
try {
    foreach ($runtime in @('win-x64', 'win-x86', 'win-arm64')) {
        $path = Join-Path $testDirectory "$runtime.exe"
        foreach ($name in @('LOL-GameGUI.exe', $entry)) {
            # Minimal PE fixture: exercise architecture and embedded entry checks without executing it.
            $bytes = [byte[]]::new(512)
            [BitConverter]::GetBytes([uint16]0x5a4d).CopyTo($bytes, 0)
            [BitConverter]::GetBytes([int32]0x80).CopyTo($bytes, 0x3c)
            [BitConverter]::GetBytes([uint32]0x4550).CopyTo($bytes, 0x80)
            $machine = @{ 'win-x64'=0x8664; 'win-x86'=0x014c; 'win-arm64'=0xaa64 }[$runtime]
            [BitConverter]::GetBytes([uint16]$machine).CopyTo($bytes, 0x84)
            # Deliberately unaligned UTF-16 string.
            [Text.Encoding]::Unicode.GetBytes($name + [char]0).CopyTo($bytes, 0x101)
            [IO.File]::WriteAllBytes($path, $bytes)
            $resolve = { Resolve-UpdateUpdater -Repository $repository -Runtime $runtime -UpdaterPath $path }
            if ($name -eq $entry) {
                if ((& $resolve) -ne $path) { throw 'Incorrect updater selected.' }
                $otherRuntime = if ($runtime -eq 'win-x64') { 'win-x86' } else { 'win-x64' }
                Assert-Rejected { Resolve-UpdateUpdater -Repository $repository -Runtime $otherRuntime -UpdaterPath $path } '与目标'
            } else {
                Assert-Rejected $resolve '更新器未包含当前入口'
            }
        }
    }
    Write-Host 'PASS: all architectures reject old entry names, accept the current entry, and reject wrong architectures.'
    # A complete app EXE is insufficient: an incomplete package would delete managed cores.
    $dependencyRoot = Join-Path $testDirectory 'dependencies'
    $hybridMarker = Join-Path $dependencyRoot 'LOL-GameAssistant/Tools/SkinCoreHybrid/league-skin-hybrid-host.exe'
    New-Item -ItemType Directory -Path (Split-Path $hybridMarker -Parent) -Force | Out-Null
    [IO.File]::WriteAllText($hybridMarker, 'fixture')
    $dependencyFiles = @(
        'Tools/SkinCore/league-skin-core-host.exe', 'Tools/SkinCore/league-skin-core.dll', 'Tools/SkinCore/SHA256SUMS.json',
        'Tools/GameOverlay/sc-game-overlay-host.exe', 'Tools/GameOverlay/sc-game-overlay.dll',
        'Tools/SkinCoreHybrid/league-skin-hybrid-host.exe', 'Tools/SkinCoreHybrid/league-skin-core.dll',
        'Tools/SkinCoreHybrid/SHA256SUMS.json', 'Tools/SkinCoreHybrid/Driver/SkinCoreRelay.sys', 'Tools/SkinCoreHybrid/Driver/manage.ps1'
    )
    foreach ($relative in $dependencyFiles) {
        $dependency = Join-Path $dependencyRoot $relative
        New-Item -ItemType Directory -Path (Split-Path $dependency -Parent) -Force | Out-Null
        [IO.File]::WriteAllText($dependency, 'fixture')
    }
    Assert-UpdateSkinDependencies -Repository $dependencyRoot -PublishDirectory $dependencyRoot
    foreach ($relative in $dependencyFiles) {
        $dependency = Join-Path $dependencyRoot $relative
        Remove-Item -LiteralPath $dependency
        Assert-Rejected { Assert-UpdateSkinDependencies -Repository $dependencyRoot -PublishDirectory $dependencyRoot } $relative
        [IO.File]::WriteAllText($dependency, '')
        Assert-Rejected { Assert-UpdateSkinDependencies -Repository $dependencyRoot -PublishDirectory $dependencyRoot } $relative
        [IO.File]::WriteAllText($dependency, 'fixture')
    }
    Assert-UpdateSkinDependencies -Repository $repository -PublishDirectory (Join-Path $repository 'LOL-GameAssistant')
    Write-Host 'PASS: complete skin dependencies accepted; every missing or empty dependency rejected.'

    # The public preflight must reject incomplete VS publish folders before
    # writing output, not defer the failure until actual ZIP creation.
    $incompleteSource = Join-Path $testDirectory 'incomplete-publish'
    New-Item -ItemType Directory -Path $incompleteSource | Out-Null
    [IO.File]::WriteAllText((Join-Path $incompleteSource $entry), 'fixture')
    $preflightOutput = Join-Path $testDirectory 'preflight-output'
    $shellPath = (Get-Process -Id $PID).Path
    $preflightResult = & $shellPath -NoProfile -File (Join-Path $repository 'tools/publish-update-menu.ps1') `
        x64 -CheckOnly -SkipInstaller -PublishDirectory $incompleteSource -OutputRoot $preflightOutput 2>&1
    if ($LASTEXITCODE -eq 0 -or ($preflightResult | Out-String) -notlike '*换肤依赖*') {
        throw 'CheckOnly did not reject the incomplete publish folder.'
    }
    if (Test-Path -LiteralPath $preflightOutput) { throw 'CheckOnly created output for an invalid package.' }
    Write-Host 'PASS: public CheckOnly rejects incomplete publish folders without creating output.'
} finally {
    if ($incompleteSource -and (Test-Path -LiteralPath $incompleteSource)) {
        Remove-Item -LiteralPath (Join-Path $incompleteSource $entry)
        Remove-Item -LiteralPath $incompleteSource
    }
    if ($dependencyRoot -and (Test-Path -LiteralPath $dependencyRoot)) {
        $resolvedDependencies = [IO.Path]::GetFullPath($dependencyRoot)
        $expectedDependencies = [IO.Path]::GetFullPath((Join-Path $testDirectory 'dependencies'))
        if ($resolvedDependencies -ne $expectedDependencies -or -not $resolvedDependencies.StartsWith([IO.Path]::GetFullPath($testDirectory) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Unexpected dependency test cleanup path.'
        }
        Remove-Item -LiteralPath $resolvedDependencies -Recurse
    }
    # Only the exact, uniquely created test files and their empty directory are removed.
    foreach ($runtime in @('win-x64', 'win-x86', 'win-arm64')) {
        $path = Join-Path $testDirectory "$runtime.exe"
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    }
    Remove-Item -LiteralPath $testDirectory
}
