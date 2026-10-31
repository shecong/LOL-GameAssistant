using Panel = AntdUI.Panel;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>日志与运行诊断共用的左侧导航入口。</summary>
internal sealed class LogToolsForm : UserControl, IThemeAware
{
    private readonly AntdUI.Segmented _navigation = new()
    {
        Dock = DockStyle.Left, Width = 200, Vertical = true, Full = false, SelectIndex = 0,
        Font = new Font(UiMetrics.FontFamily, 11F, FontStyle.Bold)
    };
    private readonly Panel _content = new() { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 0, 0) };

    public LogToolsForm(InfoMsgForm messages, Func<Control> diagnostics)
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(12);
        _navigation.Items.Add(new AntdUI.SegmentedItem { Text = "运行日志" });
        _navigation.Items.Add(new AntdUI.SegmentedItem { Text = "运行诊断" });
        _navigation.Items.Add(new AntdUI.SegmentedItem { Text = "流量监控" });
        messages.Dock = DockStyle.Fill;
        _content.Controls.Add(messages);
        Control? diagnosticPage = null;
        Control? trafficPage = null;
        _navigation.SelectIndexChanged += (_, e) =>
        {
            if (e.Value == 1 && diagnosticPage == null)
            {
                diagnosticPage = new DeferredPageHost(diagnostics) { Dock = DockStyle.Fill };
                _content.Controls.Add(diagnosticPage);
            }
            if (e.Value == 2 && trafficPage == null)
            {
                trafficPage = new TrafficMonitorForm { Dock = DockStyle.Fill };
                _content.Controls.Add(trafficPage);
            }
            if (trafficPage != null) trafficPage.Visible = e.Value == 2;
            messages.Visible = e.Value == 0;
            if (diagnosticPage != null) diagnosticPage.Visible = e.Value == 1;
        };
        Controls.Add(_content);
        Controls.Add(_navigation);
        UiTheme.Apply(this);
    }

    public void ApplyTheme(ThemePalette palette)
    {
        BackColor = _content.BackColor = palette.Surface;
        _navigation.BackColor = palette.SurfaceRaised;
        _navigation.ForeColor = palette.TextPrimary;
    }
}
