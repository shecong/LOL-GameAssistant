using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

internal sealed class PenguPluginStatusForm : AntdUI.Window
{
    private readonly IPenguPluginService _service;
    private readonly Action<string> _report;
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed;
    private readonly AntdUI.Input _details = new()
    {
        Name = "penguCheckDetails", Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
        Text = "正在检查插件文件、客户端进程、Loader 模块和插件心跳…"
    };
    private readonly AntdUI.Button _refresh = new() { Name = "refreshPenguCheck", Text = "重新检查", AutoSize = true, Height = 34 };

    internal PenguPluginStatusForm(IPenguPluginService service, Action<string> report)
    {
        _service = service;
        _report = report;
        Text = "插件加载检查";
        Size = new Size(780, 580);
        MinimumSize = new Size(580, 430);
        StartPosition = FormStartPosition.CenterParent;
        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        content.Controls.Add(_details);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(12) };
        actions.Controls.Add(_refresh);
        Controls.Add(content);
        Controls.Add(actions);
        AntdWindowChrome.AddHeader(this);
        _refresh.Click += async (_, _) => await CheckAsync();
        Shown += async (_, _) => await CheckAsync();
        Disposed += (_, _) =>
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _lifetime.Cancel();
            _lifetime.Dispose();
        };
        UiTheme.Apply(this);
    }

    private async Task CheckAsync()
    {
        if (!_refresh.Enabled) return;
        _refresh.Enabled = false;
        _details.Text = "正在检查插件文件、客户端进程、Loader 模块和插件心跳…";
        try
        {
            var result = await Task.Run(_service.Check, _lifetime.Token);
            if (IsDisposed) return;
            string message = $"检查完成：{DateTime.Now:HH:mm:ss}\n\n{result.Message}";
            _details.Text = message;
            _report(message);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (IsDisposed) return;
            _details.Text = $"检查失败：{error.Message}\n请检查所选插件目录和访问权限后重试。";
            _report(_details.Text);
        }
        finally { if (!IsDisposed) _refresh.Enabled = true; }
    }
}
