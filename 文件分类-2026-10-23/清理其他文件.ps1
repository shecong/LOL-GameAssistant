$ErrorActionPreference='Stop'
$otherRoot=[IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$otherPrefix=$otherRoot.TrimEnd('\')+'\'
$otherOriginal=@(Import-Csv -LiteralPath (Join-Path $PSScriptRoot '全部文件.csv'))
$otherKnown=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$otherOriginal | Where-Object Category -eq '其他文件' | ForEach-Object { [void]$otherKnown.Add($_.RelativePath) }
$otherActive=@(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($otherPrefix,[StringComparison]::OrdinalIgnoreCase) } | ForEach-Object { Split-Path $_.ExecutablePath -Parent })
$otherQueue=[Collections.Generic.Stack[string]]::new()
$otherQueue.Push($otherRoot)
$otherCandidates=[Collections.Generic.List[object]]::new()
$otherEmpty=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$otherScanErrors=[Collections.Generic.List[string]]::new()
while ($otherQueue.Count) {
    $otherDir=$otherQueue.Pop()
    try { $otherChildren=@(Get-ChildItem -LiteralPath $otherDir -Force -ErrorAction Stop) } catch { $otherScanErrors.Add("${otherDir}: $($_.Exception.Message)"); continue }
    foreach ($otherItem in $otherChildren) {
        $otherRelative=[IO.Path]::GetRelativePath($otherRoot,$otherItem.FullName).Replace('\','/')
        if ($otherItem.FullName -eq $PSScriptRoot -or $otherRelative -match '^(\.git|\.claude)(/|$)|^LOL-GameAssistant/Tools(/|$)|^\.artifacts/(wdk-nuget|skin-core-backup-2026-10-23|driver-test-signed|updates)(/|$)|^LOL-GameApi/data(/|$)') { continue }
        if ($otherItem.Attributes -band [IO.FileAttributes]::ReparsePoint) { $otherScanErrors.Add("跳过链接：$($otherItem.FullName)"); continue }
        if ($otherItem.PSIsContainer) { $otherQueue.Push($otherItem.FullName); continue }
        $otherIsCandidate=$otherKnown.Contains($otherRelative) -or $otherRelative -match '^(tmp|\.artifacts|\.vs)/|\.(user|suo|rsuser|pyc)$|(^|/)__pycache__/' -or ($otherRelative -match '(^|/)(bin|obj|TestResults|BenchmarkDotNet\.Artifacts)/' -and $otherRelative -notmatch '(^|/)vendor/')
        if (-not $otherIsCandidate) { continue }
        $otherStatus='待删除'
        foreach ($otherRuntimeDir in $otherActive) {
            if ($otherItem.FullName.StartsWith($otherRuntimeDir.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { $otherStatus='跳过运行中的目录' }
        }
        $otherCandidates.Add([pscustomobject]@{Status=$otherStatus;RelativePath=$otherRelative;Bytes=$otherItem.Length;Note='';FullPath=$otherItem.FullName})
    }
}
if ($otherScanErrors.Count) { $otherScanErrors | Set-Content -LiteralPath (Join-Path $PSScriptRoot '其他文件扫描异常.txt'); throw '扫描有异常，尚未删除' }
$otherCandidates | Export-Csv -LiteralPath (Join-Path $PSScriptRoot '其他文件本次清理计划.csv') -NoTypeInformation -Encoding utf8BOM
foreach ($otherCandidate in $otherCandidates) {
    if ($otherCandidate.Status -ne '待删除') { continue }
    $otherTarget=[IO.Path]::GetFullPath($otherCandidate.FullPath)
    if (-not $otherTarget.StartsWith($otherPrefix,[StringComparison]::OrdinalIgnoreCase)) { throw "目标越界：$otherTarget" }
    try {
        Remove-Item -LiteralPath $otherTarget -Force -ErrorAction Stop
        $otherCandidate.Status='已删除'
        [void]$otherEmpty.Add((Split-Path $otherTarget -Parent))
    } catch { $otherCandidate.Status='被占用或删除失败'; $otherCandidate.Note=$_.Exception.Message }
}
# 仅从删除文件的父目录开始移除空目录；全部使用工作区内的完整路径，不递归删除。
foreach ($otherEmptyDir in @($otherEmpty | Sort-Object Length -Descending)) {
    $otherParent=$otherEmptyDir
    while ($otherParent.StartsWith($otherPrefix,[StringComparison]::OrdinalIgnoreCase) -and $otherParent -ne $PSScriptRoot) {
        if (-not (Test-Path -LiteralPath $otherParent -PathType Container)) { break }
        if (@(Get-ChildItem -LiteralPath $otherParent -Force).Count) { break }
        try { Remove-Item -LiteralPath $otherParent -Force -ErrorAction Stop } catch { break }
        $otherParent=Split-Path $otherParent -Parent
    }
}
$otherCandidates | Export-Csv -LiteralPath (Join-Path $PSScriptRoot '其他文件清理执行记录.csv') -NoTypeInformation -Encoding utf8BOM
$otherCandidates | Group-Object Status | ForEach-Object { [pscustomobject]@{Status=$_.Name;Count=$_.Count;MiB=[math]::Round(($_.Group | Measure-Object Bytes -Sum).Sum/1MB,2)} } | Format-Table
$otherProtectedMissing=@($otherOriginal | Where-Object { $_.Category -ne '其他文件' -and $_.Action -ne '可重新生成' -and $_.RelativePath -notlike '.git/*' } | Where-Object { -not (Test-Path -LiteralPath $_.FullPath -PathType Leaf) })
$otherProtectedMissing | Export-Csv -LiteralPath (Join-Path $PSScriptRoot '其他文件清理后保留项缺失.csv') -NoTypeInformation -Encoding utf8BOM
"项目和部署保留文件缺失数=$($otherProtectedMissing.Count)"
