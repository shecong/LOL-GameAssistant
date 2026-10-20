#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Position = 0)][ValidateSet('x64', 'x86', 'arm64', 'all')][string]$Architecture,
    [ValidateSet('Existing', 'Build')][string]$Mode = 'Existing',
    [string]$PublishDirectory,
    [string]$PublishRoot = 'E:\releases',
    [string]$OutputRoot = 'E:\release-assets',
    [string]$ReleaseNotesFile,
    [switch]$RebuildUpdater,
    [string]$UpdaterPath,
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'

try {
    $releaseRepository = Split-Path $PSScriptRoot -Parent
    . (Join-Path $PSScriptRoot 'UpdatePublishing.ps1')
    $releaseInteractive = -not $Architecture
    if ($releaseInteractive) {
        Write-Host '自动更新打包：1=x64，2=x86（32 位），3=ARM64，4=全部架构'
        $releaseChoice = Read-Host '请选择'
        $Architecture = @{ '1'='x64'; '2'='x86'; '3'='arm64'; '4'='all' }[$releaseChoice]
        if (-not $Architecture) { throw '选择无效，请输入 1、2、3 或 4。' }
        Write-Host '1=打包 VS 已发布的文件夹（默认），2=自动构建主程序并打包'
        $releaseMode = Read-Host '请选择发布方式'
        if ($releaseMode -eq '2') { $Mode = 'Build' }
        elseif ($releaseMode -and $releaseMode -ne '1') { throw '发布方式无效。' }
    }

    $Architecture = $Architecture.ToLowerInvariant()
    [xml]$releaseVersionConfig = Get-Content -LiteralPath (Join-Path $releaseRepository 'build\Version.props') -Raw
    $releaseVersion = [string]$releaseVersionConfig.Project.PropertyGroup.SoftwareVersion
    $releaseIndex = New-UpdateIndexJson -Version $releaseVersion -ReleaseNotesFile $ReleaseNotesFile
    if ($releaseVersion -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
        throw '请先在 build/Version.props 设置正确的 SoftwareVersion，例如 1.0.2。'
    }
    $releaseSettings = Get-Content -LiteralPath (Join-Path $releaseRepository 'LOL-GameAssistant\update-source.json') -Raw | ConvertFrom-Json
    $releaseArchitectures = if ($Architecture -eq 'all') { @('x64','x86','arm64') } else { @($Architecture) }
    if ($PublishDirectory -and ($Architecture -eq 'all' -or $Mode -eq 'Build')) {
        throw '-PublishDirectory 仅用于单个架构的 Existing 模式；全部架构请设置 -PublishRoot。'
    }
    if ($UpdaterPath -and ($Architecture -eq 'all' -or $RebuildUpdater)) {
        throw '-UpdaterPath 仅用于单个架构的复用模式，不能与 -RebuildUpdater 同时使用。'
    }
    if ($RebuildUpdater) {
        foreach ($releaseArch in $releaseArchitectures) {
            & (Join-Path $releaseRepository 'native\Updater\build.ps1') -Runtime ('win-' + $releaseArch) -CheckOnly
        }
    }
    $releaseTasks = @()
    foreach ($releaseArch in $releaseArchitectures) {
        $releaseRuntime = 'win-' + $releaseArch
        $releaseSource = $null
        if ($Mode -eq 'Existing') {
            $releaseCandidates = @()
            if ($PublishDirectory) { $releaseCandidates += $PublishDirectory }
            else {
                $releaseCandidates += Join-Path $PublishRoot "$releaseVersion\$releaseRuntime\publish"
                $releaseCandidates += Join-Path $PublishRoot "$releaseVersion\$releaseRuntime"
                # 兼容目前已发布在 E:\releases\1.0.1 根部的 x64 完整包。
                if ($releaseArch -eq 'x64') { $releaseCandidates += Join-Path $PublishRoot $releaseVersion }
            }
            $releaseSource = $releaseCandidates | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'LOL-GameGUI.exe') -PathType Leaf } | Select-Object -First 1
            if (-not $releaseSource -and $releaseInteractive) {
                $releaseSource = (Read-Host "找不到 $releaseRuntime 发布包，请输入 VS 实际发布目录").Trim().Trim('"')
            }
            if (-not $releaseSource -or -not (Test-Path -LiteralPath (Join-Path $releaseSource 'LOL-GameGUI.exe') -PathType Leaf)) {
                throw "找不到 $releaseRuntime 主程序。先用 VS 发布到 $PublishRoot\$releaseVersion\$releaseRuntime\publish，或通过 -PublishDirectory 指定实际目录；自动构建请使用 -Mode Build。"
            }
            $releaseSource = [IO.Path]::GetFullPath($releaseSource)
            foreach ($releaseUserPath in @('.update','logs','cache','userdata','settings.json')) {
                if (Test-Path -LiteralPath (Join-Path $releaseSource $releaseUserPath)) {
                    throw "发布目录包含运行数据 $releaseUserPath，不能直接打包。请用 VS 发布到干净目录，或改用 -Mode Build 自动构建；不要删除正在使用的更新事务或个人配置。"
                }
            }
            $releaseEntry = Join-Path $releaseSource 'LOL-GameGUI.exe'
            $releaseInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($releaseEntry)
            if ($releaseInfo.FileVersion -ne "$releaseVersion.0") {
                throw "$releaseEntry 版本是 $($releaseInfo.FileVersion)，配置要求 $releaseVersion.0，请重新发布。"
            }
            $releaseStream = [IO.File]::OpenRead($releaseEntry)
            $releaseReader = [IO.BinaryReader]::new($releaseStream)
            try {
                if ($releaseReader.ReadUInt16() -ne 0x5a4d) { throw '主程序不是有效的 Windows EXE。' }
                $releaseStream.Position = 0x3c
                $releaseOffset = $releaseReader.ReadInt32()
                if ($releaseOffset -lt 0x40 -or $releaseOffset -gt $releaseStream.Length - 6) { throw '主程序 PE 头无效。' }
                $releaseStream.Position = $releaseOffset
                if ($releaseReader.ReadUInt32() -ne 0x4550) { throw '主程序 PE 签名无效。' }
                $releaseMachine = $releaseReader.ReadUInt16()
                if ($releaseMachine -ne @{ 'x64'=0x8664; 'x86'=0x14c; 'arm64'=0xaa64 }[$releaseArch]) {
                    throw "$releaseEntry 实际架构与 $releaseRuntime 不同，不能共用其他架构的发布包。"
                }
            } finally { $releaseReader.Dispose(); $releaseStream.Dispose() }
        }
        $releaseExistingUpdater = if (-not $RebuildUpdater) {
            Resolve-UpdateUpdater -Repository $releaseRepository -Runtime $releaseRuntime -PublishDirectory $releaseSource -UpdaterPath $UpdaterPath
        } else { $null }
        $releaseTasks += [pscustomobject]@{ Runtime=$releaseRuntime; Source=$releaseSource; Updater=$releaseExistingUpdater }
    }

    $releaseRunRoot = [IO.Path]::GetFullPath((Join-Path $OutputRoot $releaseVersion))
    foreach ($releaseTask in $releaseTasks) {
        if ($releaseTask.Source -and ($releaseRunRoot.Equals($releaseTask.Source, [StringComparison]::OrdinalIgnoreCase) -or $releaseRunRoot.StartsWith($releaseTask.Source.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))) {
            throw '打包输出不能位于输入发布目录内部，请修改 -OutputRoot。'
        }
        foreach ($releaseAsset in @("LOL-GameAssistant-$($releaseTask.Runtime).zip", "update-$($releaseTask.Runtime).json")) {
            if (Test-Path -LiteralPath (Join-Path $releaseRunRoot $releaseAsset)) { throw "已有本版本的 $releaseAsset，请升级版本号或通过 -OutputRoot 更换输出目录。" }
        }
    }
    Write-Host "版本：$releaseVersion；方式：$Mode；服务器：$($releaseSettings.serverBaseUrl)"
    $releaseTasks | Format-Table Runtime,Source,Updater -AutoSize | Out-Host
    Write-Host "本次输出目录：$releaseRunRoot"
    if ($Architecture -in @('arm64','all')) { Write-Host '默认复用对应架构的现成更新器，无需 C++ 编译器；各架构的 OCR、SkinCore、GameOverlay 依赖仍需兼容性验收。' }
    if ($CheckOnly) { Write-Host '检查完成，未构建或生成更新包。'; exit 0 }

    $releaseSummary = @("版本：$releaseVersion", "下载服务器：$($releaseSettings.serverBaseUrl)", '请先上传所有版本/架构目录，验证后最后更新 IIS 根目录 update.json。', '')
    foreach ($releaseTask in $releaseTasks) {
        $releaseOutput = $releaseRunRoot
        $releaseArguments = @{ Runtime=$releaseTask.Runtime; OutputDirectory=$releaseOutput; FlatOutput=$true }
        if ($RebuildUpdater) { $releaseArguments.RebuildUpdater = $true }
        else { $releaseArguments.UpdaterPath = $releaseTask.Updater }
        if ($releaseTask.Source) { $releaseArguments.PublishDirectory = $releaseTask.Source }
        if ($releaseSettings.serverBaseUrl) {
            $releaseArguments.ServerBaseUrl = [string]$releaseSettings.serverBaseUrl
            $releaseArguments.AllowHttpServer = ($releaseSettings.allowHttp -eq $true)
        }
        & (Join-Path $PSScriptRoot 'publish-update.ps1') @releaseArguments
        $releaseSummary += @(
            "[$($releaseTask.Runtime)]",
            "ZIP：$releaseOutput\LOL-GameAssistant-$($releaseTask.Runtime).zip",
            "完整配置：$releaseOutput\update-$($releaseTask.Runtime).json",
            "IIS：上传到 v$releaseVersion/$($releaseTask.Runtime)/，完整配置在服务器重命名为 update.json。",
            'GitHub：ZIP 与对应 update-win-*.json 直接作为同版本 Release 附件上传。', ''
        )
    }
    [IO.File]::WriteAllText((Join-Path $releaseRunRoot 'update.json'), $releaseIndex, [Text.UTF8Encoding]::new($false))
    $releaseSummary += @("IIS 根索引：$releaseRunRoot\update.json（所有架构上传并验证后最后发布）。", '旧 x64 客户端的 GitHub 兼容：可另外复制 update-win-x64.json 并以 update.json 为附件名上传；不要使用只有版本号的根索引。')
    $releaseSummaryPath = Join-Path $releaseRunRoot '发布结果.txt'
    [IO.File]::WriteAllLines($releaseSummaryPath, [string[]]$releaseSummary, [Text.UTF8Encoding]::new($false))
    Write-Host "全部所选任务成功，上传路径说明：$releaseSummaryPath"
} catch {
    [Console]::Error.WriteLine('发布未完成：' + $_.Exception.Message)
    exit 1
}
