function Get-UpdateEntryExecutable {
    param([Parameter(Mandatory)][string]$Repository)
    [xml]$project = Get-Content -LiteralPath (Join-Path $Repository 'LOL-GameAssistant\LOL-GameGUI.csproj') -Raw
    $assemblyName = [string]($project.Project.PropertyGroup.AssemblyName | Where-Object { $_ } | Select-Object -First 1)
    $entry = $assemblyName + '.exe'
    # 客户端和原生更新器都限定入口名称，不能只改发布脚本或 AssemblyName。
    if ($entry -cne 'sc助手.exe') {
        throw "主程序入口为 $entry，但当前自动更新协议要求 sc助手.exe。改名时必须同步客户端、原生更新器并重新构建。"
    }
    return $entry
}

function Assert-UpdateUpdaterEntry {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$EntryExecutable)
    # 原生更新器内嵌允许的 UTF-16 入口名称。按字节检查，避免字符串对齐影响；
    # 这是旧二进制兼容性检查，包完整性仍由 SHA256 和客户端校验负责。
    $binary = [Text.Encoding]::Latin1.GetString([IO.File]::ReadAllBytes($Path))
    $expected = [Text.Encoding]::Latin1.GetString([Text.Encoding]::Unicode.GetBytes($EntryExecutable + [char]0))
    if (-not $binary.Contains($expected, [StringComparison]::Ordinal)) {
        throw "更新器未包含当前入口 $EntryExecutable，可能仍为改名前的旧版：$Path。请使用 -RebuildUpdater 重新编译对应架构，或通过 -UpdaterPath 指定已同步名称的更新器；ARM64 构建需要安装 MSVC ARM64 工具。"
    }
}

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
        Assert-UpdateUpdaterEntry -Path $candidate -EntryExecutable (Get-UpdateEntryExecutable -Repository $Repository)
        return [IO.Path]::GetFullPath($candidate)
    }
    throw "未找到现成的 $Runtime Updater.exe。请放入 LOL-GameAssistant\Tools\Updater\$Runtime\，或使用 -UpdaterPath 指定文件。仅首次构建或更新器代码变更时才需使用 -RebuildUpdater。"
}
