function New-UpdateIndexJson {
    param([Parameter(Mandatory)][string]$Version, [string]$ReleaseNotesFile)
    $index = [ordered]@{ schemaVersion = 1; version = $Version }
    if ($ReleaseNotesFile) {
        $index.releaseNotes = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $ReleaseNotesFile).Path, [Text.Encoding]::UTF8)
    }
    $json = $index | ConvertTo-Json
    if ([Text.Encoding]::UTF8.GetByteCount($json) -gt 65536) {
        throw '更新索引超过客户端的 64 KB 限制，请缩短更新说明。'
    }
    return $json
}

function Get-UpdateExecutableRuntime {
    param([Parameter(Mandatory)][string]$Path)
    $binary = [IO.File]::OpenRead($Path)
    $reader = [IO.BinaryReader]::new($binary)
    try {
        if ($reader.ReadUInt16() -ne 0x5a4d) { throw "Not a Windows executable: $Path" }
        $binary.Position = 0x3c
        $peOffset = $reader.ReadInt32()
        if ($peOffset -lt 0x40 -or $peOffset -gt $binary.Length - 6) { throw "Invalid PE header: $Path" }
        $binary.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x4550) { throw "Invalid PE signature: $Path" }
        switch ($reader.ReadUInt16()) {
            0x8664 { 'win-x64' }
            0x014c { 'win-x86' }
            0xaa64 { 'win-arm64' }
            default { throw "Unsupported executable architecture: $Path" }
        }
    } finally { $reader.Dispose(); $binary.Dispose() }
}

function Resolve-UpdateUpdater {
    param(
        [Parameter(Mandatory)][string]$Repository,
        [Parameter(Mandatory)][string]$Runtime,
        [string]$PublishDirectory,
        [string]$UpdaterPath
    )
    $candidates = @()
    if ($UpdaterPath) { $candidates += [IO.Path]::GetFullPath($UpdaterPath) }
    else {
        $candidates += Join-Path $Repository "LOL-GameAssistant\Tools\Updater\$Runtime\Updater.exe"
        if ($PublishDirectory) { $candidates += Join-Path $PublishDirectory 'Updater.exe' }
    }
    foreach ($candidate in $candidates) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
        $actualRuntime = Get-UpdateExecutableRuntime -Path $candidate
        if ($actualRuntime -ne $Runtime) { throw "更新器实际架构为 $actualRuntime，与目标 $Runtime 不匹配：$candidate" }
        return [IO.Path]::GetFullPath($candidate)
    }
    throw "未找到现成的 $Runtime Updater.exe。请放入 LOL-GameAssistant\Tools\Updater\$Runtime\，或使用 -UpdaterPath 指定文件。仅首次构建或更新器代码变更时才需使用 -RebuildUpdater。"
}
