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

function Export-UpdateServerLayout {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$AssetDirectory,
        [string]$OutputDirectory,
        [ValidateSet('win-x64', 'win-x86', 'win-arm64')][string[]]$Runtime = @('win-x64', 'win-x86', 'win-arm64'),
        [switch]$SkipInstaller
    )
    $assets = [IO.Path]::GetFullPath($AssetDirectory)
    if (-not $OutputDirectory) { $OutputDirectory = Join-Path $assets 'server' }
    $destination = [IO.Path]::GetFullPath($OutputDirectory)
    if ($assets.TrimEnd('\','/').Equals($destination.TrimEnd('\','/'), [StringComparison]::OrdinalIgnoreCase)) {
        throw '服务器输出目录不能与 GitHub 成品目录相同。'
    }
    $indexPath = Join-Path $assets 'update.json'
    $index = Get-Content -LiteralPath $indexPath -Raw | ConvertFrom-Json
    $version = [string]$index.version
    if ($index.schemaVersion -ne 1 -or $version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
        throw 'GitHub 成品目录缺少有效的根更新索引。'
    }
    $copies = [Collections.Generic.List[object]]::new()
    # 所有架构先验证，再复制；只整理成品，不解压或重新压缩 ZIP。
    foreach ($target in ($Runtime | Select-Object -Unique)) {
        $manifestPath = Join-Path $assets "update-$target.json"
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $zipName = "LOL-GameAssistant-$target.zip"
        $zipPath = Join-Path $assets $zipName
        if ($manifest.schemaVersion -ne 1 -or $manifest.version -ne $version -or
            $manifest.runtime -ne $target -or $manifest.package -cne $zipName -or
            $manifest.entryExecutable -cne 'sc助手.exe' -or $manifest.sha256 -notmatch '^[a-fA-F0-9]{64}$') {
            throw "更新清单与版本或架构不匹配：$manifestPath"
        }
        if ((Get-Item -LiteralPath $zipPath).Length -ne $manifest.size -or
            (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash -ine $manifest.sha256) {
            throw "ZIP 大小或 SHA256 校验失败：$zipPath"
        }
        $folder = Join-Path $destination "v$version/$target"
        $copies.Add(@{ Source = $zipPath; Destination = (Join-Path $folder $zipName) })
        $copies.Add(@{ Source = $manifestPath; Destination = (Join-Path $folder 'update.json') })
        if (-not $SkipInstaller) {
            $setupName = "sc-assistant-setup-$version-$target.exe"
            $setupPath = Join-Path $assets $setupName
            $hashPath = "$setupPath.sha256"
            $expectedHash = (Get-Content -LiteralPath $hashPath -Raw).Trim() -split '\s+', 2
            if ($expectedHash.Count -ne 2 -or $expectedHash[0] -notmatch '^[a-fA-F0-9]{64}$' -or
                $expectedHash[1] -cne $setupName -or
                (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash -ine $expectedHash[0]) {
                throw "安装包 SHA256 校验失败：$setupPath"
            }
            $copies.Add(@{ Source = $setupPath; Destination = (Join-Path $folder $setupName) })
            $copies.Add(@{ Source = $hashPath; Destination = (Join-Path $folder "$setupName.sha256") })
        }
    }
    foreach ($copy in $copies) {
        New-Item -ItemType Directory -Path (Split-Path $copy.Destination -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $copy.Source -Destination $copy.Destination -Force
    }
    # 根索引最后写入，服务器路径严格对应客户端的 v{version}/{runtime}/update.json。
    Copy-Item -LiteralPath $indexPath -Destination (Join-Path $destination 'update.json') -Force
    return $destination
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

function Assert-UpdateSkinDependencies {
    param([Parameter(Mandatory)][string]$Repository, [Parameter(Mandatory)][string]$PublishDirectory)
    # 更新器会删除旧清单中缺失的文件；不能把漏拷贝的换肤依赖当作有意移除。
    $required = @(
        'Tools/SkinCore/league-skin-core-host.exe',
        'Tools/SkinCore/league-skin-core.dll',
        'Tools/SkinCore/SHA256SUMS.json',
        'Tools/GameOverlay/sc-game-overlay-host.exe',
        'Tools/GameOverlay/sc-game-overlay.dll'
    )
    if (Test-Path -LiteralPath (Join-Path $Repository 'LOL-GameAssistant/Tools/SkinCoreHybrid/league-skin-hybrid-host.exe') -PathType Leaf) {
        $required += @(
            'Tools/SkinCoreHybrid/league-skin-hybrid-host.exe',
            'Tools/SkinCoreHybrid/league-skin-core.dll',
            'Tools/SkinCoreHybrid/SHA256SUMS.json',
            'Tools/SkinCoreHybrid/Driver/SkinCoreRelay.sys',
            'Tools/SkinCoreHybrid/Driver/manage.ps1'
        )
    }
    foreach ($relative in $required) {
        $dependency = Join-Path $PublishDirectory $relative
        if (-not (Test-Path -LiteralPath $dependency -PathType Leaf) -or (Get-Item -LiteralPath $dependency).Length -eq 0) {
            throw "更新包缺少或包含空的换肤依赖：$relative。请重新发布完整目录，不能只打包主程序；否则更新可能移除旧核心并导致换肤失效。"
        }
    }
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
