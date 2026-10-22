using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

internal sealed class PracticeGoalForm : AntdUI.Window, IThemeAware
{
    internal PracticeGoal? Goal { get; private set; }
    internal PracticeGoalForm(PlayerGrowthSnapshot snapshot, IChampionCatalog champions, PracticeGoal? existing)
    {
        Text = "我的练习目标"; ClientSize = new(560, 410); StartPosition = FormStartPosition.CenterParent; Font = new Font(UiMetrics.FontFamily, 10);
        var mode = new AntdUI.Select { Name = "goalMode", List = true, DropDownArrow = true, Width = 200, Height = 36 };
        mode.Items.AddRange(snapshot.Modes.Where(m => m != "全部").Cast<object>().ToArray());
        if (existing != null && !mode.Items.Contains(existing.Mode)) mode.Items.Add(existing.Mode);
        mode.SelectedIndex = 0; if (existing != null) mode.SelectedValue = existing.Mode;
        var hero = new AntdUI.Select { Name = "goalChampion", List = true, DropDownArrow = true, Width = 200, Height = 36 };
        int[] ids = new[] { 0 }.Concat(snapshot.Report.Matches.Select(m => m.ChampionId).Distinct()).ToArray();
        hero.Items.AddRange(ids.Select(id => (object)(id == 0 ? "不限英雄" : champions.GetDisplayName(id))).ToArray());
        hero.SelectedIndex = Math.Max(0, Array.IndexOf(ids, existing?.ChampionId ?? 0));
        var metric = new AntdUI.Select { Name = "goalMetric", List = true, DropDownArrow = true, Width = 200, Height = 36 };
        metric.Items.AddRange(Enum.GetValues<PracticeMetric>().Select(m => (object)CompanionExperience.MetricName(m)).ToArray()); metric.SelectedIndex = (int)(existing?.Metric ?? PracticeMetric.Cs);
        var target = new AntdUI.InputNumber { Name = "goalTarget", Minimum = 0, Maximum = 100000, DecimalPlaces = 2, Width = 200, Height = 36, Value = (decimal)Math.Clamp(existing?.Target ?? 0, 0, 100000) };
        var count = new AntdUI.InputNumber { Name = "goalGames", Minimum = 1, Maximum = 30, Value = existing?.Games ?? 5, Width = 200, Height = 36 };
        var status = new AntdUI.Label { TextMultiLine = true, Dock = DockStyle.Fill };
        var rows = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 7, Padding = new Padding(16) };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120)); rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        int row = 0;
        foreach (var pair in new (string Text, Control Control)[] { ("模式", mode), ("英雄范围", hero), ("指标", metric), ("目标值", target), ("练习局数", count) })
        { rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); rows.Controls.Add(new AntdUI.Label { Text = pair.Text, Dock = DockStyle.Fill }, 0, row); rows.Controls.Add(pair.Control, 1, row++); }
        void Hint()
        {
            var values = snapshot.Report.Matches.Where(m => m.Mode == mode.SelectedValue?.ToString() && (hero.SelectedIndex <= 0 || m.ChampionId == ids[hero.SelectedIndex]))
                .Take(10).Select(m => CompanionExperience.Value(m, (PracticeMetric)metric.SelectedIndex));
            var mean = PlayerGrowthCalculator.Average(values);
            status.Text = $"个人最近样本均值：{(mean.HasValue ? mean.Value.ToString("0.##") : "暂无")}。目标由你选择；死亡次数为不超过，其余为至少。只统计保存后开始的对局，缺失指标不计失败。";
        }
        mode.SelectedIndexChanged += (_, _) => Hint(); hero.SelectedIndexChanged += (_, _) => Hint(); metric.SelectedIndexChanged += (_, _) => Hint();
        rows.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); rows.Controls.Add(status, 0, row); rows.SetColumnSpan(status, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46 };
        var save = new AntdUI.Button { Name = "saveGoal", Text = "保存并开始新一轮", AutoSize = true };
        save.Click += (_, _) =>
        {
            if (mode.SelectedValue is not string selected || (target.Value <= 0 && metric.SelectedIndex != (int)PracticeMetric.Deaths)) { status.Text = "请选择模式并填写有效目标值。"; return; }
            Goal = new((PracticeMetric)metric.SelectedIndex, (double)target.Value, selected, hero.SelectedIndex <= 0 ? null : ids[hero.SelectedIndex], DateTimeOffset.UtcNow, (int)count.Value);
            DialogResult = DialogResult.OK; Close();
        };
        var clear = new AntdUI.Button { Text = "结束当前目标", AutoSize = true }; clear.Click += (_, _) => { Goal = null; DialogResult = DialogResult.OK; Close(); };
        buttons.Controls.AddRange([save, clear]); Controls.Add(rows); Controls.Add(buttons); AntdWindowChrome.AddHeader(this); Hint(); UiTheme.Apply(this);
    }
    public void ApplyTheme(ThemePalette palette) => AntdContentTheme.Apply(this, palette);
}
