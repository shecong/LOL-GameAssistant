using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>Read-only diagnostics view for LCU, client start, hotkeys and local automation.</summary>
public sealed class DiagnosticsForm : UserControl, IThemeAware
{
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill,
        FullRowSelect = true,
        GridLines = true,
        View = View.Details,
        HideSelection = false
    };

    private readonly Label _hint = new()
    {
        Dock = DockStyle.Top,
        Height = 34,
        Padding = new Padding(10, 8, 10, 0),
        Text = "这里只显示本机运行状态，不记录 LCU Token、聊天内容或 API Key。"
    };

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1500 };

    public DiagnosticsForm()
    {
        _list.Columns.Add("组件", 150);
        _list.Columns.Add("状态", 110);
        _list.Columns.Add("详情", 620);
        _list.Columns.Add("更新时间", 155);
        Controls.Add(_list);
        Controls.Add(_hint);
        _timer.Tick += (_, _) => RefreshSnapshot();
        _timer.Start();
        RuntimeDiagnostics.Changed += DiagnosticsChanged;
        Disposed += (_, _) =>
        {
            _timer.Dispose();
            RuntimeDiagnostics.Changed -= DiagnosticsChanged;
        };
        RefreshSnapshot();
    }

    public void ApplyTheme(ThemePalette palette)
    {
        BackColor = palette.Surface;
        _list.BackColor = palette.SurfaceRaised;
        _list.ForeColor = palette.TextPrimary;
        _hint.BackColor = palette.Surface;
        _hint.ForeColor = palette.TextSecondary;
    }

    private void DiagnosticsChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) BeginInvoke(RefreshSnapshot);
        else RefreshSnapshot();
    }

    private void RefreshSnapshot()
    {
        if (IsDisposed) return;
        var entries = RuntimeDiagnostics.Snapshot();
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (DiagnosticEntry entry in entries)
            {
                _list.Items.Add(new ListViewItem(new[]
                {
                    entry.Component,
                    entry.Status,
                    entry.Detail,
                    entry.UpdatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")
                }));
            }
        }
        finally
        {
            _list.EndUpdate();
        }
    }
}