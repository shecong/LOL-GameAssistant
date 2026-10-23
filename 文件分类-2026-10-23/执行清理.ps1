param([switch]$Execute)
$ErrorActionPreference = 'Stop'
$cleanRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$cleanPrefix = $cleanRoot.TrimEnd('\') + '\'
$cleanCandidates = @(Import-Csv -LiteralPath (Join-Path $PSScriptRoot '优先清理候选.csv'))
$cleanInventory = @(Import-Csv -LiteralPath (Join-Path $PSScriptRoot '全部文件.csv'))
$cleanResults = [Collections.Generic.List[object]]::new()
$cleanEmptyDirs = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$cleanProcesses = @(Get-CimInstance Win32_Process)
$cleanActiveRoots = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($cleanProcess in $cleanProcesses) {
    if ($cleanProcess.ProcessId -eq $PID) { continue }
    if ($cleanProcess.ExecutablePath -and $cleanProcess.ExecutablePath.StartsWith($cleanPrefix,[StringComparison]::OrdinalIgnoreCase)) {
        $cleanExecutableRelative = [IO.Path]::GetRelativePath($cleanRoot,$cleanProcess.ExecutablePath)
        if ($cleanExecutableRelative -match '^(.*?\\bin)\\') { [void]$cleanActiveRoots.Add((Join-Path $cleanRoot $Matches[1])) }
        else { [void]$cleanActiveRoots.Add((Split-Path $cleanProcess.ExecutablePath -Parent)) }
    }
    if ($cleanProcess.Name -match '^(dotnet|MSBuild|cl|link)\.exe$' -and $cleanProcess.CommandLine -like '*weather-app*') {
        throw "发现可能进行中的构建，先停止清理：$($cleanProcess.Name) PID=$($cleanProcess.ProcessId)"
    }
    if ($cleanProcess.Name -match '^(pwsh|powershell)\.exe$' -and $cleanProcess.CommandLine -match '(?i)-File\s+[^\r\n]*publish-update[^\r\n]*\.ps1') {
        throw '发现可能进行中的发布，先停止清理'
    }
}
$cleanTracked = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
git -C $cleanRoot -c core.quotepath=false ls-files | ForEach-Object { [void]$cleanTracked.Add($_) }
foreach ($cleanRow in $cleanCandidates) {
    $cleanTarget = [IO.Path]::GetFullPath($cleanRow.FullPath)
    if (-not $cleanTarget.StartsWith($cleanPrefix,[StringComparison]::OrdinalIgnoreCase)) { throw "路径超出工作区：$cleanTarget" }
    $cleanRelative = [IO.Path]::GetRelativePath($cleanRoot,$cleanTarget).Replace('\','/')
    if ($cleanRelative -ne $cleanRow.RelativePath -or $cleanRow.Action -ne '可重新生成') { throw "清单验证失败：$cleanTarget" }
    if ($cleanRelative -match '^(\.git/|\.claude/|LOL-GameAssistant/Tools/|\.artifacts/(wdk-nuget|skin-core-backup))') { throw "命中保留目录：$cleanTarget" }
    $cleanStatus=''; $cleanNote=''
    if ($cleanTracked.Contains($cleanRelative)) { $cleanStatus='跳过'; $cleanNote='当前由 Git 跟踪，保留' }
    foreach ($cleanActiveRoot in $cleanActiveRoots) {
        if ($cleanTarget.StartsWith($cleanActiveRoot.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { $cleanStatus='跳过'; $cleanNote='目录内有正在运行的程序，保留整个输出目录' }
    }
    if (-not $cleanStatus) {
        $cleanAncestor = $cleanTarget
        while ($cleanAncestor -and $cleanAncestor -ne $cleanRoot) {
            if (Test-Path -LiteralPath $cleanAncestor) {
                if ((Get-Item -LiteralPath $cleanAncestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "路径包含链接，停止：$cleanAncestor" }
            }
            $cleanAncestor = Split-Path $cleanAncestor -Parent
        }
        if (-not (Test-Path -LiteralPath $cleanTarget -PathType Leaf)) { $cleanStatus='已不存在' }
        else {
            $cleanItem = Get-Item -LiteralPath $cleanTarget -Force
            if ($cleanItem.Length -ne [long]$cleanRow.Bytes -or $cleanItem.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss') -ne $cleanRow.Modified) {
                $cleanStatus='跳过'; $cleanNote='文件在扫描后发生变化'
            } elseif (-not $Execute) { $cleanStatus='待删除' }
            else {
                try {
                    Remove-Item -LiteralPath $cleanTarget -Force -ErrorAction Stop
                    $cleanStatus='已删除'
                    [void]$cleanEmptyDirs.Add((Split-Path $cleanTarget -Parent))
                } catch { $cleanStatus='删除失败'; $cleanNote=$_.Exception.Message }
            }
        }
    }
    $cleanResults.Add([pscustomobject]@{Status=$cleanStatus;RelativePath=$cleanRelative;Bytes=[long]$cleanRow.Bytes;Note=$cleanNote;FullPath=$cleanTarget})
}
# 仅移除本次删除文件留下的空目录；不递归删除任何目录。
if ($Execute) {
    foreach ($cleanDirectory in @($cleanEmptyDirs | Sort-Object Length -Descending)) {
        $cleanParent = $cleanDirectory
        while ($cleanParent.StartsWith($cleanPrefix,[StringComparison]::OrdinalIgnoreCase) -and $cleanParent -ne $PSScriptRoot) {
            if (-not (Test-Path -LiteralPath $cleanParent -PathType Container)) { break }
            if (@(Get-ChildItem -LiteralPath $cleanParent -Force).Count) { break }
            try { Remove-Item -LiteralPath $cleanParent -Force -ErrorAction Stop } catch { break }
            $cleanParent = Split-Path $cleanParent -Parent
        }
    }
}
$cleanResultName = if ($Execute) { '清理执行记录.csv' } else { '清理预览.csv' }
$cleanResults | Export-Csv -LiteralPath (Join-Path $PSScriptRoot $cleanResultName) -NoTypeInformation -Encoding utf8BOM
$cleanResults | Group-Object Status | ForEach-Object { [pscustomobject]@{ Status=$_.Name;Count=$_.Count;GiB=[math]::Round(($_.Group | Measure-Object Bytes -Sum).Sum/1GB,3) } } | Format-Table
if ($Execute) {
    # Git 自身与 Codex 会更新或回收内部临时引用，不纳入业务文件存在性检查。
    $cleanMissingProtected = @($cleanInventory | Where-Object { $_.Action -ne '可重新生成' -and $_.RelativePath -notlike '.git/*' } | Where-Object { -not (Test-Path -LiteralPath $_.FullPath -PathType Leaf) })
    $cleanMissingProtected | Export-Csv -LiteralPath (Join-Path $PSScriptRoot '保留文件缺失检查.csv') -NoTypeInformation -Encoding utf8BOM
    "保留文件缺失数=$($cleanMissingProtected.Count)"
    if ($cleanMissingProtected.Count) { throw '存在保留文件缺失，请检查报告' }
}
