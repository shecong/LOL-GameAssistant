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
} finally {
    # Only the exact, uniquely created test files and their empty directory are removed.
    foreach ($runtime in @('win-x64', 'win-x86', 'win-arm64')) {
        $path = Join-Path $testDirectory "$runtime.exe"
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    }
    Remove-Item -LiteralPath $testDirectory
}
