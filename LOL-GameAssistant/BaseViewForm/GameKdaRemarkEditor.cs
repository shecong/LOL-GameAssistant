using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>逐人 KDA 文案编辑弹窗，保存各表现档位的候选文案。</summary>
internal sealed class GameKdaRemarkEditor : AntdUI.Window, IThemeAware
{
    private readonly Dictionary<string, AntdUI.Input> _inputs = new();
    private readonly AntdUI.Label _note;
    private readonly AntdUI.PageHeader _header;
    private readonly Image _headerIcon;

    /// <summary>初始化 GameKdaRemarkEditor 的实例状态，并保存传入的依赖或数据。</summary>
    public GameKdaRemarkEditor(IReadOnlyDictionary<string, string> current, Action<Dictionary<string, string>> save)
    {
        Text = "自定义逐人 KDA 测评文案";
        Icon = AppIcon.Shared;
        StartPosition = FormStartPosition.CenterParent;
        AntdWindowChrome.Configure(this);
        ShowInTaskbar = false;
        Size = new Size(860, 650);
        MinimumSize = Size;
        _headerIcon = AppIcon.Shared.ToBitmap();
        _header = new AntdUI.PageHeader
        {
            Text = Text, Icon = _headerIcon, ShowIcon = true, Dock = DockStyle.Top, Height = 42,
            ShowButton = true, MinimizeBox = false, UseSystemStyleColor = false, UseForeColorDrawIcons = true
        };
        _note = new AntdUI.Label
        {
            Dock = DockStyle.Top, Height = 95, Padding = new Padding(16, 6, 16, 6),
            Text = "每行一条，每个档位分别随机选句；连续两次尽量不重复。填写后优先使用自定义，留空使用内置词库。姓名、KDA 和样本量会自动保留。\n支持：{name} 玩家名、{team} 队伍、{position} 位置、{kda} KDA、{samples} 场数、{score} 评分、{label} 档位、{winrate} 胜率。\n例如：{name}，KDA才{kda}，甩锅前先看一眼自己的成绩单。每条最多 200 字。"
        };
        var rows = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(16, 4, 16, 8) };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var categories = new[] { ("Upper", "上等马"), ("Medium", "中等马"), ("Lower", "下等马"), ("Human", "纯牛马") };
        for (int index = 0; index < categories.Length; index++)
        {
            rows.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
            var (key, label) = categories[index];
            rows.Controls.Add(new AntdUI.Label { Text = label, Dock = DockStyle.Fill }, 0, index);
            var input = new AntdUI.Input { Multiline = true, Dock = DockStyle.Fill, Text = current.GetValueOrDefault(key, ""), PlaceholderText = "每行一条；留空使用此档位的内置文案" };
            _inputs[key] = input;
            rows.Controls.Add(input, 1, index);
        }
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(16, 4, 16, 4), FlowDirection = FlowDirection.RightToLeft };
        var apply = new AntdUI.Button { Text = "保存文案并应用", Width = 150, Height = 36 };
        var restore = new AntdUI.Button { Text = "清空，使用内置文案", Width = 175, Height = 36 };
        restore.Click += (_, _) => { foreach (var input in _inputs.Values) input.Text = ""; };
        apply.Click += (_, _) =>
        {
            if (_inputs.Values.Any(input => input.Lines.Any(line => line.Trim().Length > 200)))
            {
                _note.Text = "某条文案超过 200 字，请缩短后再保存。";
                return;
            }
            try
            {
                save(_inputs.ToDictionary(pair => pair.Key, pair => pair.Value.Text.Trim()));
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex) { _note.Text = $"保存失败：{ex.Message}"; }
        };
        footer.Controls.Add(apply);
        footer.Controls.Add(restore);
        Controls.Add(rows);
        Controls.Add(footer);
        Controls.Add(_note);
        Controls.Add(_header);
        UiTheme.Apply(this);
    }

    /// <summary>将语义主题颜色应用到当前控件或窗口。</summary>
    public void ApplyTheme(ThemePalette palette)
    {
        BackColor = palette.Surface;
        ForeColor = palette.TextPrimary;
        _note.ForeColor = palette.TextSecondary;
        _header.ForeColor = palette.TextPrimary;
        _header.BackColor = palette.SurfaceRaised;
        _header.ColorScheme = palette.IsDark ? AntdUI.TAMode.Dark : AntdUI.TAMode.Light;
        foreach (var input in _inputs.Values)
        {
            input.BackColor = palette.SurfaceRaised;
            input.ForeColor = palette.TextPrimary;
            input.BorderColor = palette.Border;
            input.PlaceholderColor = palette.TextSecondary;
            foreach (var label in input.Parent!.Controls.OfType<AntdUI.Label>()) label.ForeColor = palette.TextPrimary;
        }
    }

    /// <summary>释放当前对象持有的资源，结束相关事件订阅或后台任务。</summary>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _headerIcon.Dispose();
    }
}
