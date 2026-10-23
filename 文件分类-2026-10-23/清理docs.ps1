$ErrorActionPreference = 'Stop'
$docsRoot = [IO.Path]::GetFullPath('E:\code\weather-app\docs')
$docsPrefix = $docsRoot.TrimEnd('\') + '\'
$docsLicenses = Join-Path $docsRoot 'licenses'
$docsResults = [Collections.Generic.List[object]]::new()
$docsQueue = [Collections.Generic.Stack[string]]::new()
$docsQueue.Push($docsRoot)
$docsEmpty = [Collections.Generic.List[string]]::new()
$docsTargets = [Collections.Generic.List[object]]::new()
while ($docsQueue.Count) {
    $docsCurrent = $docsQueue.Pop()
    if (-not $docsCurrent.StartsWith($docsPrefix,[StringComparison]::OrdinalIgnoreCase) -and $docsCurrent -ne $docsRoot) { throw '目录超出 docs' }
    foreach ($docsEntry in Get-ChildItem -LiteralPath $docsCurrent -Force) {
        if ($docsEntry.FullName -eq $docsLicenses) { continue }
        if ($docsEntry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "发现链接，停止清理：$($docsEntry.FullName)" }
        if ($docsEntry.PSIsContainer) { $docsQueue.Push($docsEntry.FullName); $docsEmpty.Add($docsEntry.FullName) }
        else { $docsTargets.Add($docsEntry) }
    }
}
foreach ($docsEntry in $docsTargets) {
    $docsTarget = [IO.Path]::GetFullPath($docsEntry.FullName)
    if (-not $docsTarget.StartsWith($docsPrefix,[StringComparison]::OrdinalIgnoreCase) -or $docsTarget.StartsWith($docsLicenses+'\',[StringComparison]::OrdinalIgnoreCase)) { throw "目标验证失败：$docsTarget" }
    Remove-Item -LiteralPath $docsTarget -Force -ErrorAction Stop
    $docsResults.Add([pscustomobject]@{Status='已删除';FullPath=$docsTarget;Bytes=$docsEntry.Length})
}
foreach ($docsDirectory in @($docsEmpty | Sort-Object Length -Descending)) {
    if (-not $docsDirectory.StartsWith($docsPrefix,[StringComparison]::OrdinalIgnoreCase)) { throw '空目录超出 docs' }
    if (@(Get-ChildItem -LiteralPath $docsDirectory -Force).Count -eq 0) { Remove-Item -LiteralPath $docsDirectory -Force -ErrorAction Stop }
}
$docsResults | Export-Csv -LiteralPath (Join-Path $PSScriptRoot 'docs文档删除记录.csv') -NoTypeInformation -Encoding utf8BOM
"DeletedFiles=$($docsResults.Count)"
Get-ChildItem -LiteralPath $docsRoot -Recurse -File | Select-Object FullName,Length | Format-Table -AutoSize
