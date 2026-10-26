using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public sealed partial class SkinToolsForm
{
    private readonly IClientRegaliaService _clientRegalia;
    private readonly CancellationTokenSource _clientRegaliaLifetime = new();
    private ClientRegaliaState? _clientBorder;
    private bool _clientBorderBusy;
    private AntdUI.Panel? _clientBorderCard;
    private readonly AntdUI.Select _clientBorderChoices = new() { Name = "ClientBorderChoices", Dock = DockStyle.Top, Height = 46, List = true, Enabled = false, PlaceholderText = "先读取客户端边框状态" };
    private readonly AntdUI.Button _clientBorderRead = new() { Name = "ClientBorderRead", Text = "读取客户端边框", AutoSize = true, Height = 40 };
    private readonly AntdUI.Button _clientBorderApply = new() { Name = "ClientBorderApply", Text = "应用客户端边框", AutoSize = true, Height = 40, Enabled = false };
    private readonly AntdUI.Button _clientBorderRestore = new() { Name = "ClientBorderRestore", Text = "恢复客户端原样", AutoSize = true, Height = 40, Enabled = false };
    private readonly AntdUI.Label _clientBorderStatus = new() { Name = "ClientBorderStatus", Dock = DockStyle.Top, Height = 84, TextMultiLine = true,
        Text = "登录客户端后可读取；不需要开启换肤核心或进入对局。" };

    private Control CreateClientBorderCard()
    {
        _clientBorderCard = new AntdUI.Panel { Name = "ClientBorderCard", Dock = DockStyle.Top, Height = 420, Radius = 14, Padding = new Padding(24), BorderWidth = 1 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 76, AutoSize = true, WrapContents = true, Padding = new Padding(0, 12, 0, 8) };
        buttons.Controls.AddRange([_clientBorderRead, _clientBorderApply, _clientBorderRestore]);_surfaceRows.Add(buttons);
        var identity = new AntdUI.Button { Name = "ClientIdentityOpen", Text = "浏览全部身份装饰", AutoSize = true, Height = 40 };
        identity.Click += (_, _) => { using var picker = new ClientIdentityForm(LOL_GameAssistant.Bootstrap.AppCompositionRoot.ClientIdentityService);picker.ShowDialog(FindForm()); };
        buttons.Controls.Add(identity);
        _clientBorderCard.Controls.Add(_clientBorderStatus);_clientBorderCard.Controls.Add(buttons);_clientBorderCard.Controls.Add(_clientBorderChoices);
        _clientBorderCard.Controls.Add(new AntdUI.Label { Dock = DockStyle.Top, Height = 74, TextMultiLine = true,
            Text = "切换客户端现有的排位段位边框或等级头像边框，保留旗帜与其他装饰。支持恢复本次首次读取的设置；不改变真实段位，也不提供任意段位选择。" });
        _clientBorderCard.Controls.Add(new AntdUI.Label { Dock = DockStyle.Top, Height = 44, Text = "客户端头像与名片", Font = new Font(UiMetrics.FontFamily, 14, FontStyle.Bold) });
        _clientBorderChoices.Items.Add(new AntdUI.SelectItem("排位段位边框（账号实际段位）", "ranked"));
        _clientBorderChoices.Items.Add(new AntdUI.SelectItem("等级头像边框（当前等级装饰）", "prestige"));
        _clientBorderRead.Click += async (_, _) => await RunClientBorderAsync(0);
        _clientBorderApply.Click += async (_, _) => await RunClientBorderAsync(1);
        _clientBorderRestore.Click += async (_, _) => await RunClientBorderAsync(2);
        _clientBorderChoices.SelectedIndexChanged += (_, _) => UpdateClientBorderButtons();
        return _clientBorderCard;
    }
    private void UpdateClientBorderButtons()
    {
        _clientBorderRead.Enabled = !_disposing && !_clientBorderBusy;
        bool ready = !_disposing && !_clientBorderBusy && _clientBorder != null;
        _clientBorderChoices.Enabled = ready;_clientBorderRestore.Enabled = ready;
        _clientBorderApply.Enabled = ready && _clientBorderChoices.SelectedValue is string;
    }
    private async Task RunClientBorderAsync(int action)
    {
        if (_disposing || _clientBorderBusy || (action != 0 && _clientBorder == null)) return;
        _clientBorderBusy = true;UpdateClientBorderButtons();
        _clientBorderStatus.Text = "正在读取并校验客户端边框状态…";
        try
        {
            string message;
            if (action == 0)
            {
                _clientBorder = await _clientRegalia.ReadAsync(_clientRegaliaLifetime.Token);
                message = "当前偏好：" + (_clientBorder.CrestType == "ranked" ? "账号排位段位边框" : _clientBorder.CrestType == "prestige" ? "等级头像边框" : _clientBorder.CrestType) + "。读取完成，可应用或恢复。";
            }
            else
            {
                var result = action == 1
                    ? await _clientRegalia.ApplyAsync(_clientBorder!.Session, (string)_clientBorderChoices.SelectedValue!, _clientRegaliaLifetime.Token)
                    : await _clientRegalia.RestoreAsync(_clientBorder!.Session, _clientRegaliaLifetime.Token);
                _clientBorder = result.Verified ? result.State : null;message = result.Message;
            }
            if (_disposing || IsDisposed) return;
            _clientBorderStatus.Text = UiLanguage.T(message);
            if (_clientBorder != null) _clientBorderChoices.SelectedValue = _clientBorder.CrestType;
            RuntimeDiagnostics.Report("客户端边框", "处理结果", message);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            _clientBorder = null;
            if (!_disposing && !IsDisposed) _clientBorderStatus.Text = "客户端边框未完成：" + error.Message;
            RuntimeDiagnostics.Report("客户端边框", "未完成", error.Message);
        }
        finally { _clientBorderBusy = false;if (!_disposing && !IsDisposed) UpdateClientBorderButtons(); }
    }
}
