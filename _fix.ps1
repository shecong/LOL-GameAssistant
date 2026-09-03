$path = "E:\code\weather-app\LOL-GameAssistant\BaseViewForm\BattleQueryForm.cs"
$content = [System.IO.File]::ReadAllText($path)

# Step 1: Add RawGameStat class
$old1 = "private Dictionary<long, GameDetailModel.GameInfo>? _detailCache;"
$new1 = "private Dictionary<long, GameDetailModel.GameInfo>? _detailCache;
        private List<RawGameStat>? _rawGameStats;
        private bool _hasFilterButtons;

        private class RawGameStat
        {
            public string Mode = \"";
            public double Kda;
            public bool Win;
            public int ChampionId;
        }"
$content = $content.Replace($old1, $new1)
Write-Host "Step 1 done"

# Step 2: Add raw data storage
$old2 = "                    kdaTrend.Add((gameKda, win));

                    int cid = gamer.championId;"
$new2 = "                    kdaTrend.Add((gameKda, win));
                    _rawGameStats.Add(new RawGameStat { Mode = mode, Kda = gameKda, Win = win, ChampionId = gamer.championId });

                    int cid = gamer.championId;"
$content = $content.Replace($old2, $new2)
Write-Host "Step 2 done"

# Step 3: Replace chart section with rebuild call
$startMarker = "            _statsLoaded = true;"
$endMarker = "            panelStats.Controls.Add(chartContainer);"
$si = $content.IndexOf($startMarker)
$ei = $content.IndexOf($endMarker, $si)
if ($ei -ge 0) {
    $ei = $content.IndexOf("`n", $ei) + 1
}
if ($si -ge 0 -and $ei -gt 0) {
    $before = $content.Substring(0, $si)
    $after = $content.Substring($ei)
    $newPart = "            _statsLoaded = true;
            _hasFilterButtons = false;
            RebuildStatsCharts("`"全部`"");"
    $content = $before + $newPart + $after
    Write-Host "Step 3 done"
} else {
    Write-Host "Step 3 failed: si=$si ei=$ei"
}

# Step 4: Add RebuildStatsCharts method before ShowStatsFromCache
$mStart = "        private void ShowStatsFromCache()"
$mi = $content.IndexOf($mStart)
if ($mi -ge 0) {
    $before4 = $content.Substring(0, $mi)
    $after4 = $content.Substring($mi)
    $newMethod = @"
        /// <summary>
        /// 根据模式筛选重建统计图表
        /// </summary>
        private void RebuildStatsCharts(string filter)
        {
            if (_rawGameStats == null || _rawGameStats.Count == 0) return;

            var filtered = string.IsNullOrEmpty(filter) || filter == `"全部`"
                ? _rawGameStats
                : _rawGameStats.Where(s => s.Mode == filter).ToList();

            if (filtered.Count == 0)
            {
                panelStats.Controls.Clear();
                panelStats.Controls.Add(new Label { Text = "该模式暂无数据", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter });
                return;
            }

            // 计算聚合
            int totalWins = filtered.Count(s => s.Win);
            int totalLosses = filtered.Count - totalWins;
            double sumK = 0, sumD = 0, sumA = 0;  // 实际未存到 RawGameStat，使用 kdaTrend 替代
            var champStats = new Dictionary<int, (int g, int w)>();
            var queueStats = new Dictionary<string, int>();
            var kdaList = new List<(double kda, bool win)>();

            // 从 _detailCache 重新计算详细数据
            // 由于 RawGameStat 只存了合计 KDA，我们需要冠军和模式分布
            foreach (var stat in filtered)
            {
                int cid = stat.ChampionId;
                var cur = champStats.GetValueOrDefault(cid);
                champStats[cid] = (cur.g + 1, cur.w + (stat.Win ? 1 : 0));
                kdaList.Add((stat.Kda, stat.Win));

                if (!queueStats.ContainsKey(stat.Mode))
                    queueStats[stat.Mode] = 0;
                queueStats[stat.Mode]++;
            }

            double overallKda = filtered.Count > 0 ? filtered.Average(s => s.Kda) : 0;
            double winRate = filtered.Count > 0 ? Math.Round((double)totalWins / filtered.Count * 100, 1) : 0;

            // 构建 UI
            panelStats.Controls.Clear();

            // 筛选按钮栏
            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 35, Padding = new Padding(5), BackColor = Color.WhiteSmoke };
            var allModes = _rawGameStats.Select(s => s.Mode).Distinct().OrderBy(m => m).ToList();
            var filterButtons = new List<string> { `"全部`" };
            filterButtons.AddRange(allModes);
            foreach (var modeText in filterButtons)
            {
                var btn = new Button
                {
                    Text = modeText,
                    AutoSize = true,
                    Height = 26,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = modeText == filter ? Color.DodgerBlue : SystemColors.Control,
                    ForeColor = modeText == filter ? Color.White : SystemColors.ControlText,
                    Margin = new Padding(2)
                };
                var capturedMode = modeText;
                btn.Click += (_, _) => {
                    // 更新按钮样式
                    foreach (Control c in btnPanel.Controls)
                    {
                        if (c is Button b)
                        {
                            b.BackColor = b.Text == capturedMode ? Color.DodgerBlue : SystemColors.Control;
                            b.ForeColor = b.Text == capturedMode ? Color.White : SystemColors.ControlText;
                        }
                    }
                    RebuildStatsCharts(capturedMode);
                };
                btnPanel.Controls.Add(btn);
            }
            panelStats.Controls.Add(btnPanel);

            // 图表容器
            var chartContainer = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

            // KDA 趋势
            if (kdaList.Count > 1)
            {
                var trendData = kdaList.TakeLast(30).ToList();
                var kdaPanel = new Panel { Dock = DockStyle.Top, Height = 210 };
                kdaPanel.Paint += (s, e) => ChartDrawer.DrawKdaTrend(e.Graphics, kdaPanel.ClientRectangle, trendData, "KDA 趋势（近 N 场 · 绿=胜 红=负）");
                chartContainer.Controls.Add(kdaPanel);
            }

            // 英雄条形图
            if (champStats.Count > 0)
            {
                var champData = champStats
                    .OrderByDescending(x => x.Value.g).Take(10)
                    .Select(x => (ChampionMap.GetChampion(x.Key)?.RealName ?? (""英雄"" + x.Key.ToString()), x.Value.g, Math.Round((double)x.Value.w / x.Value.g * 100, 1)))
                    .ToList();
                var champPanel = new Panel { Dock = DockStyle.Top, Height = Math.Max(80, champData.Count * 25 + 30) };
                champPanel.Paint += (s, e) => ChartDrawer.DrawChampionBars(e.Graphics, champPanel.ClientRectangle, champData, ""常用英雄 Top 10"");
                chartContainer.Controls.Add(champPanel);
            }

            // 摘要
            var sb = new System.Text.StringBuilder();
            sb.Append("筛选: ").Append(filter).Append("  |  场次: ").Append(filtered.Count);
            sb.Append("  |  胜场: ").Append(totalWins).Append("  |  负场: ").Append(totalLosses).Append("  |  胜率: ").Append(winRate).Append("%");
            sb.Append("  |  平均KDA: ").Append(Math.Round(overallKda, 2));
            var summaryLabel = new Label
            {
                Text = sb.ToString(),
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 9F),
                Padding = new Padding(10, 0, 0, 0)
            };
            chartContainer.Controls.Add(summaryLabel);

            panelStats.Controls.Add(chartContainer);
            lblStatus.Text = "筛选: " + filter + "  " + filtered.Count + " 场 · 胜率 " + winRate + "%";
        }
"@
    $content = $before4 + $newMethod + $after4
    Write-Host "Step 4 done"
} else {
    Write-Host "Step 4 failed: ShowStatsFromCache not found"
}

[System.IO.File]::WriteAllText($path, $content, [System.Text.Encoding]::UTF8)
Write-Host "All done"
