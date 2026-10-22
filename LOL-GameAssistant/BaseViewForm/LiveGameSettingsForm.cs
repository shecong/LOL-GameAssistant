using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

internal sealed class LiveGameSettingsForm : AntdUI.Window, IThemeAware
{
    internal LiveGameSettingsForm(LiveHistoryFilter initial, Action<LiveHistoryFilter> save)
    {
        Text = "对局设置";
        ClientSize = new Size(520, 340);
        MinimumSize = Size;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font(UiMetrics.FontFamily, 10);
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 4
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 38, 48, 42 }) content.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(new AntdUI.Label
        {
            Text = "十场战绩筛选", Dock = DockStyle.Fill,
            Font = new Font(UiMetrics.FontFamily, 13, FontStyle.Bold)
        });
        content.Controls.Add(new AntdUI.Label
        {
            Text = "选择玩家卡片中的战绩范围，近期胜率同步更新。", Dock = DockStyle.Fill, TextMultiLine = true
        });
        var choice = new AntdUI.Select
        {
            Name = "liveHistoryFilter", AccessibleName = "十场战绩筛选", List = true,
            DropDownArrow = true, Dock = DockStyle.Fill, Radius = 6
        };
        choice.Items.AddRange(new object[] { "最近十场 · 全部模式", "当前模式 · 最近十场" });
        choice.SelectedIndex = (int)initial;
        content.Controls.Add(choice);
        var note = new AntdUI.Label { Dock = DockStyle.Fill, TextMultiLine = true, Padding = new Padding(0, 14, 0, 0) };
        void UpdateNote() => note.Text = (choice.SelectedIndex == 1
            ? "在已获取的最近 500 条战绩摘要中，按当前对局模式筛选最近十场；不足十场显示实际数量。未识别模式时不混入其他模式。"
            : "按对局时间从新到旧显示最近十场，不限制游戏模式。") + "\n\n修改后自动保存并立即生效。";
        content.Controls.Add(note);
        bool restoring = false;
        choice.SelectedIndexChanged += (_, _) =>
        {
            if (restoring || choice.SelectedIndex < 0) return;
            try
            {
                var selected = (LiveHistoryFilter)choice.SelectedIndex;
                save(selected);
                initial = selected;
            }
            catch (Exception error)
            {
                restoring = true;
                try { choice.SelectedIndex = (int)initial; }
                finally { restoring = false; }
                RuntimeDiagnostics.WriteException(error);
                UiMessage.error(this, $"设置保存失败：{error.Message}");
            }
            UpdateNote();
        };
        Controls.Add(content);
        AntdWindowChrome.AddHeader(this);
        UpdateNote();
        UiTheme.Apply(this);
    }

    public void ApplyTheme(ThemePalette palette) => AntdContentTheme.Apply(this, palette);
}
