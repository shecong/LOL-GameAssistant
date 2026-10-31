using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

internal sealed class ClientCapabilitiesForm : AntdUI.Window
{
    private readonly UiRequestScope _requests = new();
    internal ClientCapabilitiesForm(IClientCapabilityService service)
    {
        Text = "当前客户端接口能力"; Size = new Size(1100, 680); MinimumSize = new Size(700, 460); StartPosition = FormStartPosition.CenterParent;
        var table = new AntdUI.Table { Dock = DockStyle.Fill, Bordered = true };
        foreach (var (key, label) in new[] { ("Method", "方法"), ("Path", "路径"), ("State", "状态"), ("Reason", "说明") }) table.Columns.Add(new AntdUI.Column(key, label) { Width = key == "Path" ? "43%" : key == "Reason" ? "35%" : "11%" });
        var status = new AntdUI.Label { Dock = DockStyle.Bottom, Height = 70, TextMultiLine = true, Padding = new Padding(12) };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10) };
        var search = new AntdUI.Input { Width = 370, Height = 38, PlaceholderText = "搜索方法、路径或状态" }; var refresh = new AntdUI.Button { Text = "只读刷新接口目录", AutoSize = true }; bar.Controls.AddRange([search, refresh]);
        ClientCapabilitySnapshot? snapshot = null;
        string State(ClientCapabilityState s) => s switch { ClientCapabilityState.Advertised => "目录已列出", ClientCapabilityState.Observed => "请求已接受", ClientCapabilityState.AuthenticationRequired => "需重新登录", ClientCapabilityState.Forbidden => "权限/阶段限制", ClientCapabilityState.ContextUnavailable => "当前资源不存在", ClientCapabilityState.MethodUnavailable => "方法不匹配", _ => "待核实" };
        void Render() => table.DataSource = (snapshot?.Capabilities ?? []).Where(c => string.IsNullOrWhiteSpace(search.Text) || (c.Method + c.Path + State(c.State) + c.Reason).Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(c => new Row(c.Method, c.Path, State(c.State), c.Reason)).ToArray();
        async Task Load()
        {
            using var r = _requests.Begin(); refresh.Enabled = false;
            try { snapshot = await service.RefreshAsync(r.Token, true); if (IsDisposed || !r.IsCurrent) return; status.Text = $"构建 {snapshot.Build} · 地区 {snapshot.Region} · {snapshot.CheckedAt.ToLocalTime():MM-dd HH:mm}\n{snapshot.Message}；404 只表示当前资源缺失，不等于接口永久失效。"; Render(); }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!IsDisposed) status.Text = e.Message; }
            finally { if (!IsDisposed) refresh.Enabled = true; }
        }
        refresh.Click += async (_, _) => await Load(); search.TextChanged += (_, _) => Render(); Shown += async (_, _) => await Load();
        Controls.Add(table); Controls.Add(bar); Controls.Add(status); AntdWindowChrome.AddHeader(this); Disposed += (_, _) => _requests.Dispose(); UiTheme.Apply(this);
    }
    private sealed record Row(string Method, string Path, string State, string Reason);
}
