$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$outDir = $PSScriptRoot
$rootDir = 'E:\code\weather-app'
$rows = foreach ($file in Get-ChildItem "$rootDir\LOL-GameAssistant\BaseViewForm" -Filter *.cs) {
    $source = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($match in [regex]::Matches($source, 'new\s+(?:System\.Drawing\.)?Font\(\s*(?<family>"[^"]+"|UiMetrics\.FontFamily|[^,\r\n]+)\s*,\s*(?<size>\d+(?:\.\d+)?)[fF]?(?<tail>[^\r\n;]*)')) {
        [pscustomobject]@{
            Page = ($file.BaseName -replace '\.(Designer|Layout|Reference|Content|Features|Timeline|Recruitment|Preview|Overlay|Objects|Hotkeys|MatchTools|AutoSave|Companion)$', '')
            File = $file.Name
            Line = ($source.Substring(0, $match.Index).Split("`n").Length)
            Family = $match.Groups['family'].Value.Trim('"')
            Size = [double]$match.Groups['size'].Value
            Style = if ($match.Groups['tail'].Value -match 'FontStyle\.(\w+)') { $Matches[1] } else { 'Regular' }
        }
    }
}
$rows | Export-Csv "$outDir\font-declarations.csv" -NoTypeInformation -Encoding utf8
$report = [System.Collections.Generic.List[string]]::new()
$report.Add(@'
# 页面字体审查与整体替换建议

日期：2026-10-20。范围：当前工作区源代码；没有修改应用界面代码，没有启动所有窗口验证最终渲染。

## 结论

建议整体更新候选为 MiSans：正文 Regular 10.5pt，导航和分组标题使用较粗字重，页面标题 18pt。本机已安装 MiSans；发布到其他电脑时需要统一字体加载和回退。审美判断需结合实际页面验证，字体更换本身不会改变文字与背景的色值对比度。

保守方案为继续使用 Microsoft YaHei UI，统一字号与颜色。第三种方案为思源黑体 Source Han Sans SC，适合更规整的数据界面，但需部署字体并复查布局。

## 静态字体声明清单

下表包含 Designer 和运行时代码中的数值字号，可能包括被运行时覆盖的初始值，不代表所有值同时生效。没有显式字号的控件会继承容器或框架默认值，静态扫描不能确认其最终字号。完整声明位置见 font-declarations.csv。

| 页面或组件 | 声明字号（pt） | 声明字重/样式 |
|---|---|---|
'@)
foreach ($group in $rows | Group-Object Page | Sort-Object Name) {
    $sizes = ($group.Group.Size | Sort-Object -Unique) -join '、'
    $styles = ($group.Group.Style | Sort-Object -Unique) -join '、'
    $report.Add("| $($group.Name) | $sizes | $styles |")
}
$report.Add(@'

## 继承字体的页面及弹窗

以下视图未扫描到数值固定的 new Font 声明，需在实际控件树中确认容器继承和框架默认值：CoachForm、DiagnosticsForm、InfoMsgForm、QuickShoutForm、LocalCacheToolsControl、AugmentReviewForm、BackupPreviewForm、ClientCapabilitiesForm、GameKdaRemarkEditor、PartyHubForm、ProfileAppearancePickerForm、ReplayManagerForm、RewardSelectionForm。

## 主窗口、绘制组件和 HTML

- 主导航 GameMain：Microsoft YaHei UI，11pt Bold。
- 全局 UiMetrics.FontFamily：Microsoft YaHei UI，但 BaseViewForm 中有 102 处直接写字体名称的数值字号构造，另有 39 处引用全局常量的数值字号构造。仅修改常量不能完整替换；另有继承字体、变量字号和绘制字体需处理。
- BaseViewForm 数值字号构造共 141 处，其中 36 处小于 9pt；这是代码声明数量，不是页面上文字数量。
- 英雄选择伴随窗：初始 9pt，FollowClient 根据宽度动态取 8–10pt；按当前宽度限制通常约 8–8.82pt。
- 战斗浮层 BattleOverlayController：Microsoft YaHei UI，字号参数动态传入。
- ChartDrawer：Microsoft YaHei UI，标题 10pt Bold，图表标注 8pt。
- AssetDetailToolTip：Microsoft YaHei UI，10pt。
- 原生 GameOverlay：单独加载 Windows Fonts/msyh.ttc，17px；不会跟随 C# 全局常量。
- promo.html：Inter → Segoe UI → Microsoft YaHei → system-ui；普通 CSS 包含 10–21px，标题含 31–82px/响应式字号；也有 7–9px 的局部图形标注，font-size:0 属于隐藏/布局用途，不应一律放大。
- architecture.html：system-ui → -apple-system → Segoe UI；代码列 SF Mono/Cascadia Code/Consolas；主体 11–15px，章节标题 18px，主标题 32px，统计数字 36px。

## 字号规范建议

| 用途 | 建议字号 | 字重 |
|---|---|---|
| 页面主标题 | 18pt | Bold / 可用的较粗静态字重 |
| 玩家名称 | 12–14pt | Bold |
| 卡片/分组标题 | 11–12pt | Bold |
| 导航 | 11pt | 较粗字重，仅选中项强化 |
| 正文/输入/按钮 | 10–10.5pt | Regular |
| 时间/标签/辅助信息 | 9–9.5pt | Regular |
| KDA/胜率/关键数值 | 12–16pt | Bold |

WinForms 的 FontStyle 没有 Medium/Semibold 枚举；若使用这些字重，应加载合适的静态字重文件，不能假定替换字体名称就得到 500/600。对比图使用 GDI+ Regular/Bold，以实际可用接口绘制。

## 文字颜色与对比度

按不透明纯色文字/纯色背景计算，透明文字、图片、渐变和局部覆写颜色需单独确认。这不是整应用可访问性认证。以下浅色背景为 #FFFFFF；深色正文背景 #0D1D32，深色辅助背景 #12263E。

| 用途 | 当前 | 建议 | 当前 → 建议对比度 |
|---|---|---|---|
| 浅色正文 | #232D37 | #172033 | 13.98 → 16.27 |
| 浅色辅助文字 | #58636E | #475569 | 6.13 → 7.58 |
| 浅色蓝色强调文字 | #1976D2 | #1565C0 | 4.60 → 5.75 |
| 深色正文 | #EBF4FF | #F1F5F9 | 15.27 → 15.47 |
| 深色辅助文字 | #95B0CE | #B8C7DA | 6.83 → 8.90 |

architecture.html 浅色卡片 #FCFCFB 上的弱文字 #898781 约 3.50:1，建议改为 #626660 或同等深度。正文普通字号可参考 WCAG 4.5:1，常用辅助文字建议留出余量。Accent 同时可能用于文字、按钮填充及图形，实施时应拆分用途，不能盲目统一改色。

## 优先顺序

1. RecentMatchRow 的 7.5–8.5pt、LivePlayerForm 的 8pt、MatchDetailForm 的 8–8.5pt 优先提高。
2. HomeForm 的统计标签统一到 9–9.5pt，正文 10–10.5pt；保持玩家名称的视觉层级。
3. 智能建议、日志、工具、各弹窗显式设定基础字体，减少对默认字体的依赖。
4. 将字体与字号提取为语义样式，再处理 Designer、动态控件、自绘层和原生层的独立入口。
5. 更换字体后复查长中文、英文、数字、自动省略、多行文本，验证 100% / 125% / 150% DPI，调整固定卡片高度和列宽。

## 参考

- MiSans 官方：https://hyperos.mi.com/font/zh/about/
- 微软雅黑 UI：https://learn.microsoft.com/en-gb/typography/font-list/microsoft-yahei
- 思源黑体：https://github.com/adobe-fonts/source-han-sans
- 对比度计算参考：https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum
'@)
[IO.File]::WriteAllText("$outDir\字体样式审查.md", ($report -join "`r`n"), [Text.UTF8Encoding]::new($false))

$bitmap = [Drawing.Bitmap]::new(1440, 1040)
$bitmap.SetResolution(120,120)
$g = [Drawing.Graphics]::FromImage($bitmap)
$g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$g.Clear([Drawing.ColorTranslator]::FromHtml('#EDF1F6'))
function Draw-Text($text, $family, $pt, $bold, $color, $x, $y) {
    $font = [Drawing.Font]::new($family, [single]$pt, $(if($bold){[Drawing.FontStyle]::Bold}else{[Drawing.FontStyle]::Regular}))
    $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($color))
    $g.DrawString($text,$font,$brush,[single]$x,[single]$y)
    $brush.Dispose(); $font.Dispose()
}
Draw-Text '字体实字对比 · 页面整体更新候选' 'Microsoft YaHei UI' 21 $true '#172033' 42 28
Draw-Text '同一字号与色值对比；按 120 DPI 绘制，非应用页面截图。' 'Microsoft YaHei UI' 10 $false '#475569' 46 85
$families = @('Microsoft YaHei UI','MiSans','等线')
$labels = @('A  微软雅黑 UI / 兼容方案','B  MiSans / 整体更新候选','C  等线 / 本机备选')
for($i=0;$i -lt 3;$i++) {
    $x=42+$i*466
    $white=[Drawing.SolidBrush]::new([Drawing.Color]::White)
    $g.FillRectangle($white,$x,140,438,825); $white.Dispose()
    Draw-Text $labels[$i] 'Microsoft YaHei UI' 12 $true '#172033' ($x+20) 164
    Draw-Text '当前常见小字 8pt' 'Microsoft YaHei UI' 9 $false '#475569' ($x+20) 226
    Draw-Text '近期战绩  排位赛  2026-10-20' $families[$i] 8 $false '#58636E' ($x+20) 264
    Draw-Text '建议正文 10.5pt · Regular' 'Microsoft YaHei UI' 9 $false '#475569' ($x+20) 317
    Draw-Text '近期战绩  排位赛  2026-10-20' $families[$i] 10.5 $false '#172033' ($x+20) 356
    Draw-Text '玩家信息与对局数据，清晰呈现。' $families[$i] 10.5 $false '#172033' ($x+20) 394
    Draw-Text '辅助文字 9.5pt · #475569' 'Microsoft YaHei UI' 9 $false '#475569' ($x+20) 453
    Draw-Text '最近 20 场 · 胜率 65% · 已更新' $families[$i] 9.5 $false '#475569' ($x+20) 492
    Draw-Text '关键数据 15pt · Bold' 'Microsoft YaHei UI' 9 $false '#475569' ($x+20) 551
    Draw-Text '12 / 3 / 8    65.8%' $families[$i] 15 $true '#1565C0' ($x+20) 590
    $dark=[Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#12263E'))
    $g.FillRectangle($dark,($x+16),678,406,263); $dark.Dispose()
    Draw-Text '深色主题 · 分组标题' $families[$i] 12 $true '#F1F5F9' ($x+32) 707
    Draw-Text '玩家信息与对局数据' $families[$i] 10.5 $false '#F1F5F9' ($x+32) 767
    Draw-Text '最近 20 场 · 胜率 65%' $families[$i] 9.5 $false '#B8C7DA' ($x+32) 812
    Draw-Text '12 / 3 / 8' $families[$i] 15 $true '#F1F5F9' ($x+32) 861
}
Draw-Text '字体影响字形与笔画；文字/背景色值决定计算对比度。' 'Microsoft YaHei UI' 10 $false '#475569' 46 991
$bitmap.Save("$outDir\font-comparison.png", [Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bitmap.Dispose()
Write-Output "Created report, CSV ($($rows.Count) declarations), and comparison image in $outDir"
