$ErrorActionPreference = 'Stop'
$auditRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$auditOutput = $PSScriptRoot
$auditTracked = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
git -C $auditRoot -c core.quotepath=false ls-files | ForEach-Object { [void]$auditTracked.Add($_) }
$auditErrors = [Collections.Generic.List[string]]::new()
$auditLinks = [Collections.Generic.List[string]]::new()
$auditFiles = [Collections.Generic.List[object]]::new()
$auditQueue = [Collections.Generic.Stack[string]]::new()
$auditQueue.Push($auditRoot)
while ($auditQueue.Count) {
    $auditDirectory = $auditQueue.Pop()
    try { $auditChildren = @(Get-ChildItem -LiteralPath $auditDirectory -Force -ErrorAction Stop) }
    catch { $auditErrors.Add("${auditDirectory}: $($_.Exception.Message)"); continue }
    foreach ($auditFile in $auditChildren) {
        if ($auditFile.FullName -eq $auditOutput) { continue }
        if ($auditFile.Attributes -band [IO.FileAttributes]::ReparsePoint) { $auditLinks.Add($auditFile.FullName); continue }
        if ($auditFile.PSIsContainer) { $auditQueue.Push($auditFile.FullName); continue }
        $auditPath = [IO.Path]::GetRelativePath($auditRoot, $auditFile.FullName).Replace('\','/')
        $auditCategory = '项目文件'; $auditAction = '保留'; $auditReason = '源码、资源、测试、开发工具或项目说明；不能仅按扩展名判定无用'
        if ($auditPath -match '^\.git/') {
            $auditReason = '主仓库版本历史与元数据，禁止手动清理'
        } elseif ($auditPath -match '^\.claude/worktrees/') {
            $auditReason = '独立 Git 工作树，存在未提交修改；保留，不能当临时目录删除'
        } elseif ($auditPath -match '^\.artifacts/wdk-nuget/') {
            $auditReason = '驱动构建脚本引用的本地 WDK 依赖，保留'
        } elseif ($auditPath -match '^\.artifacts/skin-core-backup-2026-10-23/') {
            $auditReason = '含源码和运行文件的恢复备份；确认恢复需求结束前保留'
        } elseif ($auditPath -match '^LOL-GameApi/data/') {
            $auditReason = '服务端运行数据，可能包含 SQLite 数据库及 WAL，保留并备份'
        } elseif ($auditPath -match '^\.artifacts/updates/pack-work/') {
            $auditCategory = '部署文件'; $auditAction = '可重新生成'; $auditReason = '发布脚本生成的临时 publish/package 工作副本；结束发布且不再用于手工打包后可清理'
        } elseif ($auditPath -match '^LOL-GameAssistant/Tools/' -or $auditPath -match '^\.artifacts/(updates|driver-test-signed)/') {
            $auditCategory = '部署文件'; $auditReason = '运行依赖、更新器、发布包或测试签名部署材料；按需保留，不作为垃圾'
        } elseif ($auditPath -match '(^|/)(bin|obj|TestResults|BenchmarkDotNet\.Artifacts)/' -and $auditPath -notmatch '(^|/)vendor/') {
            $auditCategory = '其他文件'; $auditAction = '可重新生成'; $auditReason = '构建或测试输出；关闭程序后可清理，首次运行或发布前需重新构建'
        } elseif ($auditPath -match '^\.vs/' -or $auditPath -match '\.(user|suo|rsuser)$' -or $auditPath -match '(^|/)__pycache__/|\.pyc$') {
            $auditCategory = '其他文件'; $auditAction = '可重新生成'; $auditReason = 'IDE 个人状态或解释器缓存；删除会重置本机状态'
        } elseif ($auditPath -match '^tmp/') {
            $auditCategory = '其他文件'; $auditAction = '确认后归档或删除'; $auditReason = '临时探测、实验源码和分析依赖；未发现正式构建引用，但可能包含独有研究记录'
        } elseif ($auditPath -match '^\.artifacts/') {
            $auditCategory = '其他文件'; $auditAction = '确认后归档或删除'; $auditReason = '检查截图、日志、实验和历史审计材料；不属于正常应用运行依赖，可能有留档价值'
        } elseif ($auditPath -match '(^|/)Properties/PublishProfiles/' -or $auditPath -match '^build/(iis-update\.web\.config|release-notes\.example\.txt|update\.example\.json)$' -or $auditPath -match '^tools/(publish-update(-menu)?|UpdatePublishing)\.ps1$' -or $auditPath -match '^(publish-update\.bat|UPDATE\.md|自动更新操作手册\.md)$' -or $auditPath -match '^LOL-GameAssistant/update-source\.json$' -or $auditPath -match '^native/Updater/' -or $auditPath -match '^\.github/') {
            $auditCategory = '部署文件'; $auditReason = '发布入口、发布配置、版本更新文档或更新器源码'
        } elseif ($auditPath -match '^docs/audit-' -or $auditPath -match '^docs/(IT审计报告|未使用代码清单)' -or $auditPath -match '^项目文件清理') {
            $auditCategory = '其他文件'; $auditAction = '确认后归档或删除'; $auditReason = '历史审计与清理记录；不参与正常构建，可按留档需求处理'
        } elseif ($auditPath -eq 'promo.html' -or $auditPath -eq 'architecture.html') {
            $auditReason = '项目宣传页或架构说明，属于项目辅助资料；不用时可单独归档'
        } elseif ($auditPath -match '^build/') {
            $auditReason = '主项目直接导入的版本或 OCR 依赖配置，必须保留；build 不是产物目录'
        } elseif ($auditPath -match '(^|/)(licenses|vendor)/|LICENSE|THIRD.PARTY|PROVENANCE') {
            $auditReason = '第三方源码、许可证或来源记录，构建与分发需要保留'
        } elseif ($auditPath -match '^native/LeagueSkinCore/[^/]+\.json$') {
            $auditReason = '原生核心配置或适配验证材料；不能仅按 JSON 判定无用，保留'
        }
        $auditFiles.Add([pscustomobject]@{ Category=$auditCategory; Action=$auditAction; RelativePath=$auditPath; Bytes=$auditFile.Length; MiB=[math]::Round($auditFile.Length/1MB,4); TrackedByMainGit=$auditTracked.Contains($auditPath); Modified=$auditFile.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'); Reason=$auditReason; FullPath=$auditFile.FullName })
    }
}
$auditSorted = @($auditFiles | Sort-Object Category,RelativePath)
$auditSorted | Export-Csv -LiteralPath (Join-Path $auditOutput '全部文件.csv') -NoTypeInformation -Encoding utf8BOM
foreach ($auditKind in @('项目文件','部署文件','其他文件')) {
    $auditSorted | Where-Object Category -eq $auditKind | Export-Csv -LiteralPath (Join-Path $auditOutput "$auditKind.csv") -NoTypeInformation -Encoding utf8BOM
}
$auditSorted | Where-Object Action -eq '可重新生成' | Export-Csv -LiteralPath (Join-Path $auditOutput '优先清理候选.csv') -NoTypeInformation -Encoding utf8BOM
$auditSorted | Where-Object Action -eq '确认后归档或删除' | Export-Csv -LiteralPath (Join-Path $auditOutput '需确认留档价值.csv') -NoTypeInformation -Encoding utf8BOM
$auditSummary = @($auditSorted | Group-Object Category,Action | ForEach-Object { [pscustomobject]@{ Category=$_.Group[0].Category; Action=$_.Group[0].Action; Count=$_.Count; Bytes=($_.Group | Measure-Object Bytes -Sum).Sum } })
$auditTop = @($auditSorted | Group-Object { ($_.RelativePath -split '/')[0] } | ForEach-Object { [pscustomobject]@{ Directory=$_.Name; Count=$_.Count; MiB=[math]::Round(($_.Group | Measure-Object Bytes -Sum).Sum/1MB,2) } } | Sort-Object MiB -Descending)
$auditTop | Export-Csv -LiteralPath (Join-Path $auditOutput '顶层目录统计.csv') -NoTypeInformation -Encoding utf8BOM
$auditGroups = @($auditSorted | Where-Object Action -eq '可重新生成' | Group-Object { if ($_.RelativePath -like '.artifacts/updates/pack-work/*') { '.artifacts/updates/pack-work' } elseif ($_.RelativePath -match '^(.*?/(?:bin|obj|TestResults|__pycache__))/') { $Matches[1] } elseif ($_.RelativePath -like '.vs/*') { '.vs' } else { $_.RelativePath } } | ForEach-Object { [pscustomobject]@{ Path=$_.Name; Count=$_.Count; MiB=[math]::Round(($_.Group | Measure-Object Bytes -Sum).Sum/1MB,2) } } | Sort-Object MiB -Descending)
$auditGroups | Export-Csv -LiteralPath (Join-Path $auditOutput '可重建目录统计.csv') -NoTypeInformation -Encoding utf8BOM
[pscustomobject]@{ Files=$auditSorted.Count; Bytes=($auditSorted | Measure-Object Bytes -Sum).Sum; Summary=$auditSummary; Errors=@($auditErrors); SkippedLinks=@($auditLinks); ExcludedReportDirectory=$auditOutput; SnapshotUtc=[datetime]::UtcNow.ToString('o') } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $auditOutput '扫描摘要.json') -Encoding utf8
$auditSummary | Format-Table -AutoSize
$auditTop | Format-Table -AutoSize
$auditGroups | Select-Object -First 15 | Format-Table -AutoSize
"Errors=$($auditErrors.Count); SkippedLinks=$($auditLinks.Count)"
