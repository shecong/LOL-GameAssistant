using ToolTip = LOL_GameAssistant.Helper.UiToolTip;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Application.Settings;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>Read-only diagnostics view for LCU, client start, hotkeys and local automation.</summary>
public sealed class DiagnosticsForm : UserControl, IThemeAware
{
    private readonly IApplicationSettingsStore _settings;
    private readonly AntdUI.Switch _enabled = new() { Name = "RuntimeDiagnosticsEnabled", Width = 52, Height = 30 };
    private bool _updatingEnabled;
    private readonly AntdUI.Table _list = new()
    {
        Dock = DockStyle.Fill,
        Bordered = true
    };

    private readonly AntdUI.Label _hint = new()
    {
        Dock = DockStyle.Top,
        Height = 60, TextMultiLine = true,
        Padding = new Padding(10, 8, 10, 0),
        Text = "本机运行状态；诊断日志不会记录 LCU Token、聊天内容或 API Key。悬停查看日志路径。"
    };

    private readonly ToolTip _logTip = new();

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1500 };

    /// <summary>初始化 DiagnosticsForm 的实例状态。</summary>
    public DiagnosticsForm() : this(null) { }
    public DiagnosticsForm(IApplicationSettingsStore? settings)
    {
        _settings = settings ?? LOL_GameAssistant.Bootstrap.AppCompositionRoot.ApplicationSettingsStore;
        _enabled.Checked = _settings.Load().RuntimeDiagnosticsEnabled;
        RuntimeDiagnostics.SetEnabled(_enabled.Checked);
        _list.Columns.Add(new AntdUI.Column(nameof(DiagnosticRow.Component), "组件") { Width = "150" });
        _list.Columns.Add(new AntdUI.Column(nameof(DiagnosticRow.Status), "状态") { Width = "110" });
        _list.Columns.Add(new AntdUI.Column(nameof(DiagnosticRow.Detail), "详情") { Width = "620" });
        _list.Columns.Add(new AntdUI.Column(nameof(DiagnosticRow.UpdatedAt), "更新时间") { Width = "155" });
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 94, Padding = new Padding(8), WrapContents = true };
        var capability = new AntdUI.Button { Text = "刷新接口能力", AutoSize = true };
        capability.Click += async (_, _) => { capability.Enabled = false; try { var result = await LOL_GameAssistant.Bootstrap.AppCompositionRoot.ClientCapabilities.RefreshAsync(force: true); if (!IsDisposed) { RuntimeDiagnostics.Report("客户端接口能力", result.HasSchema ? "已读取" : "待核实", $"{result.Message} · 构建 {result.Build} · 地区 {result.Region}"); } } catch (Exception e) { if (!IsDisposed) RuntimeDiagnostics.Report("客户端接口能力", "读取失败", e.Message); } finally { if (!IsDisposed) capability.Enabled = true; } };
        var pause = new AntdUI.Button { Text = "本局暂停自动化", AutoSize = true }; pause.Click += (_, _) => LOL_GameAssistant.Bootstrap.AppCompositionRoot.Automation.Pause();
        var resume = new AntdUI.Button { Text = "恢复自动化", AutoSize = true }; resume.Click += (_, _) => { if (FindForm() is GameMain main) main.ResumeAutomations(); else LOL_GameAssistant.Bootstrap.AppCompositionRoot.Automation.Resume(); };
        var retry = new AntdUI.Button { Text = "重试赛后失败步骤", AutoSize = true }; retry.Click += async (_, _) => await LOL_GameAssistant.Bootstrap.AppCompositionRoot.Automation.RetryFailedAsync();
        actions.Controls.AddRange([capability, pause, resume, retry]);
        var directory = new AntdUI.Button { Text = "查看接口可用原因", AutoSize = true };
        directory.Click += (_, _) => { using var form = new ClientCapabilitiesForm(LOL_GameAssistant.Bootstrap.AppCompositionRoot.ClientCapabilities); form.ShowDialog(FindForm()); };
        actions.Controls.Add(directory);
        Controls.Add(_list);
        Controls.Add(actions);
        Controls.Add(_hint);
        var enabledRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 54, Padding = new Padding(8), WrapContents = false };
        enabledRow.Controls.Add(new AntdUI.Label { Text = "启用运行诊断", Width = 140, Height = 30 });
        enabledRow.Controls.Add(_enabled); Controls.Add(enabledRow);
        _enabled.CheckedChanged += (_, _) => ToggleDiagnostics();
        _logTip.SetToolTip(_hint, RuntimeDiagnostics.GetLogPath());
        _timer.Tick += (_, _) => { if (RuntimeDiagnostics.Enabled && PageActivity.IsActive(this)) RefreshSnapshot(); };
        VisibleChanged += (_, _) => UpdateDiagnosticsActivity();
        RuntimeDiagnostics.Changed += DiagnosticsChanged;
        Disposed += (_, _) =>
        {
            _timer.Dispose();
            _logTip.Dispose();
            RuntimeDiagnostics.Changed -= DiagnosticsChanged;
        };
        UpdateDiagnosticsActivity();
    }

    private void ToggleDiagnostics()
    {
        if (_updatingEnabled) return;
        try
        {
            var settings = _settings.Load(); settings.RuntimeDiagnosticsEnabled = _enabled.Checked;
            _settings.Save(settings);
            RuntimeDiagnostics.SetEnabled(_enabled.Checked);
            if (_enabled.Checked) RuntimeDiagnostics.ReportStartup();
            UpdateDiagnosticsActivity();
        }
        catch (Exception error)
        {
            _updatingEnabled = true;
            try { _enabled.Checked = RuntimeDiagnostics.Enabled; } finally { _updatingEnabled = false; }
            _hint.Text = "保存运行诊断设置失败：" + error.Message;
        }
    }

    private void UpdateDiagnosticsActivity()
    {
        if (IsDisposed || Disposing) return;
        _timer.Enabled = Visible && RuntimeDiagnostics.Enabled;
        _hint.Text = RuntimeDiagnostics.Enabled ? "运行诊断已开启。状态与诊断日志仅在开启期间记录。"
            : "运行诊断默认关闭，不采集状态或写入诊断日志。需要排查问题时再开启。";
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
        if (!RuntimeDiagnostics.Enabled || IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) BeginInvoke(() => { if (PageActivity.IsActive(this)) RefreshSnapshot(); });
        else if (PageActivity.IsActive(this)) RefreshSnapshot();
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
