import re

with open(r'E:\code\weather-app\LOL-GameAssistant\BaseViewForm\BattleQueryForm.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# 1. Add kdaTrend list
content = content.replace(
    'var queueStats = new Dictionary<string, int>();',
    'var queueStats = new Dictionary<string, int>();\n            var kdaTrend = new List<(double kda, bool win)>();'
)

# 2. Add per-game KDA calc
old = '                    sumKills += gamer.stats.kills;\n                    sumDeaths += gamer.stats.deaths;\n                    sumAssists += gamer.stats.assists;\n\n                    int cid = gamer.championId;'
new = '''                    sumKills += gamer.stats.kills;
                    sumDeaths += gamer.stats.deaths;
                    sumAssists += gamer.stats.assists;

                    double gameKda = gamer.stats.deaths > 0
                        ? Math.Round((double)(gamer.stats.kills + gamer.stats.assists) / gamer.stats.deaths, 2)
                        : (gamer.stats.kills + gamer.stats.assists);
                    kdaTrend.Add((gameKda, win));

                    int cid = gamer.championId;'''
content = content.replace(old, new)

# 3. Replace end section
old_end_start = '            _statsLoaded = true;'
old_end_end = '            lblStatus.Text = $'
idx1 = content.index(old_end_start)
idx2 = content.index(old_end_end, idx1)
idx2 = content.index('\n', idx2)  # end of that line

# Find the next non-blank line after the label/panel lines
old_end = content[idx1:idx2+1]
# Actually let me find the full old end section

new_end = '''            _statsLoaded = true;

            // 构建统计图表 UI
            double kda = sumDeaths > 0 ? Math.Round((sumKills + sumAssists) / sumDeaths, 2) : (sumKills + sumAssists);
            double winRate = totalGames > 0 ? Math.Round((double)totalWins / totalGames * 100, 1) : 0;

            var chartContainer = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

            // KDA 趋势图表
            if (kdaTrend.Count > 1)
            {
                var trendData = kdaTrend.TakeLast(30).ToList();
                var kdaPanel = new Panel { Dock = DockStyle.Top, Height = 210 };
                kdaPanel.Paint += (s, e) => ChartDrawer.DrawKdaTrend(e.Graphics, kdaPanel.ClientRectangle, trendData, "KDA 趋势（近 N 场 · 绿=胜 红=负）");
                chartContainer.Controls.Add(kdaPanel);
            }

            // 常用英雄条形图
            if (championStats.Count > 0)
            {
                var champData = championStats
                    .OrderByDescending(x => x.Value.games).Take(10)
                    .Select(x => (ChampionMap.GetChampion(x.Key)?.RealName or "英雄" + x.Key, x.Value.games, round(x.Value.wins / x.Value.games * 100, 1)))
                    .ToList();
                var champPanel = new Panel { Dock = DockStyle.Top, Height = max(80, champData.Count * 25 + 30) };
                champPanel.Paint += (s, e) => ChartDrawer.DrawChampionBars(e.Graphics, champPanel.ClientRectangle, champData, "常用英雄 Top 10");
                chartContainer.Controls.Add(champPanel);
            }

            // 摘要信息
            var sb = new System.Text.StringBuilder();
            sb.Append("总场次: " + totalGames + "  |  胜场: " + totalWins + "  |  负场: " + totalLosses + "  |  胜率: " + winRate + "%");
            sb.Append("  |  KDA: " + kda);
            var ordered = sorted(queueStats.items(), key=lambda x: x[1], reverse=True)
            var parts = [q[0] + "=" + q[1] for q in ordered]
            sb.Append("  |  模式: " + "/".join(parts))

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

            panelStats.Controls.Clear();
            panelStats.Controls.Add(chartContainer);
            lblStatus.Text = "近 " + totalGames + " 场 · 胜率 " + winRate + "% · KDA " + kda;'''

# This approach won't work cleanly with Python string replacements for C# multiline
# Let me save what we have and try a different approach
print("Step 1 and 2 done, writing partial changes")
with open(r'E:\code\weather-app\LOL-GameAssistant\BaseViewForm\BattleQueryForm.cs', 'w', encoding='utf-8') as f:
    f.write(content)
print("Written")
