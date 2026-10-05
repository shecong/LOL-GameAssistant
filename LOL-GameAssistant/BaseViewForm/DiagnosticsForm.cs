using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>Read-only diagnostics view for LCU, client start, hotkeys and local automation.</summary>
public sealed class DiagnosticsForm : UserControl, IThemeAware
{
    private readonly AntdUI.Table _list = new()
    {
        Dock = DockStyle.Fill,
        Bordered = true
    };

    private readonly AntdUI.Label _hint = new()
    {
        Dock = DockStyle.Top,
        Height = 34,
        Padding = new Padding(10, 8, 10, 0),
        Text = "本机运行状态；诊断日志不会记录 LCU Token、聊天内容或 API Key。悬停查看日志路径。"
    };

    private readonly ToolTip _logTip = new();

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1500 };

    /// <summary>初始化 DiagnosticsForm 的实例状态。</summary>
    public DiagnosticsForm()
    {
        _list.Columns.Add(new AntdUI.Column(nameof(DiagnosticRow.Component), "组件") { Width = "150" });
        _list.Columns.Add(new AntdUI.Column(nameof(DiagnosticRow.Status), "状态") { Width = "110" });
        _list.Columns.Add(new AntdUI.Column(nameof(DiagnosticRow.Detail), "详情") { Width = "620" });
        _list.Columns.Add(new AntdUI.Column(nameof(DiagnosticRow.UpdatedAt), "更新时间") { Width = "155" });
        Controls.Add(_list);
        Controls.Add(_hint);
        _logTip.SetToolTip(_hint, RuntimeDiagnostics.GetLogPath());
        _timer.Tick += (_, _) => RefreshSnapshot();
        _timer.Start();
        RuntimeDiagnostics.Changed += DiagnosticsChanged;
        Disposed += (_, _) =>
        {
            _timer.Dispose();
            _logTip.Dispose();
            RuntimeDiagnostics.Changed -= DiagnosticsChanged;
        };
        RefreshSnapshot();
    }

    /// <summary>将语义主题颜色应用到当前控件或窗口。</summary>
    public void ApplyTheme(ThemePalette palette)
    {
        BackColor = palette.Surface;
        _list.BackColor = palette.SurfaceRaised;
        _list.ForeColor = palette.TextPrimary;
        _hint.BackColor = palette.Surface;
        _hint.ForeColor = palette.TextSecondary;
    }

    /// <summary>诊断记录变化后刷新诊断列表。</summary>
    private void DiagnosticsChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) BeginInvoke(RefreshSnapshot);
        else RefreshSnapshot();
    }

    /// <summary>将最新诊断快照同步到界面。</summary>
    private void RefreshSnapshot()
    {
        if (IsDisposed) return;
        _list.DataSource = RuntimeDiagnostics.Snapshot()
            .Select(entry => new DiagnosticRow(entry.Component, entry.Status, entry.Detail,
                entry.UpdatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")))
            .ToArray();
    }

    /// <summary>诊断列表中的一行展示数据。</summary>
    private sealed record DiagnosticRow(string Component, string Status, string Detail, string UpdatedAt);
}
