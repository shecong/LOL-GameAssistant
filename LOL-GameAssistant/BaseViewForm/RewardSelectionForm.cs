using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

internal sealed class RewardSelectionForm : AntdUI.Window, IThemeAware
{
    private readonly IClientFeatureService _features;
    private readonly UiRequestScope _requests = new();
    private readonly FlowLayoutPanel _content = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16) };
    private readonly AntdUI.Label _status = new() { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(16) };
    private bool _busy;
    internal RewardSelectionForm(IClientFeatureService features)
    {
        _features = features; Text = "待领取奖励 · 扫描与领取"; Size = new Size(760, 620); MinimumSize = new Size(720, 450); StartPosition = FormStartPosition.CenterParent;
        var refresh = new AntdUI.Button { Text = "重新扫描奖励", Dock = DockStyle.Top, Height = 45 }; refresh.Click += async (_, _) => await RefreshAsync();
        var claimAll = new AntdUI.Button { Text = "领取全部无需选择的奖励", Dock = DockStyle.Top, Height = 45 };
        claimAll.Click += async (_, _) =>
        {
            if (_busy) return;
            _busy = true; claimAll.Enabled = false;
            string message = "";
            try { message = (await _features.ClaimAllPendingRewardsAsync(_requests.Token)).Message; }
            catch (OperationCanceledException) { }
            catch (Exception e) { message = e.Message; }
            finally { _busy = false; if (!claimAll.IsDisposed) claimAll.Enabled = true; }
            if (!IsDisposed) { await RefreshAsync(); if (!IsDisposed) _status.Text = message; }
        };
        var shop = new AntdUI.Button { Text = "魄罗商店 · 勾选多个物品兑换", Dock = DockStyle.Top, Height = 45 };
        shop.Click += (_, _) => { if (!_busy) new PoroShopForm(_features).Show(this); };
        Controls.Add(_content); Controls.Add(_status); Controls.Add(shop); Controls.Add(claimAll); Controls.Add(refresh); AntdWindowChrome.AddHeader(this);
        Shown += async (_, _) => await RefreshAsync(); Disposed += (_, _) => _requests.Dispose(); UiTheme.Apply(this);
    }
    private async Task RefreshAsync()
    {
        if (_busy || IsDisposed) return; _busy = true;
        try
        {
            _status.Text = "正在扫描商城每日箱、战令、待选奖励，并同步好运魄罗签到…";
            var scan = await _features.ScanRewardsAsync(_requests.Token); if (IsDisposed) return;
            var grants = scan.Rewards;
            ControlLifetime.ClearAndDispose(_content);
            foreach (var grant in grants)
            {
                var group = new FlowLayoutPanel { Width = 650, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12), Margin = new Padding(0, 0, 0, 15) };
                group.Controls.Add(new AntdUI.Label { Text = $"[{grant.Source}] {grant.Title}" + (grant.RequiresSelection ? $" · 请选择 {grant.MinimumSelections}～{grant.AvailableChoices} 项" : ""), AutoSize = true });
                if (grant.Description.Length > 0) group.Controls.Add(new AntdUI.Label { Text = grant.Description, Width = 610, Height = 50 });
                var checks = new List<(AntdUI.Checkbox Box, string Id)>();
                foreach (var choice in grant.Choices.Count > 0 ? grant.Choices : grant.RewardIds.Select(id => new ClientRewardChoice(id, id, "未提供名称与说明")).ToArray())
                {
                    var box = new AntdUI.Checkbox { Text = $"{choice.Name} · {choice.Description}", Width = 610, Height = 48, Checked = grant.RewardIds.Count == 1 };
                    checks.Add((box, choice.Id)); group.Controls.Add(box);
                }
                var claim = new AntdUI.Button { Text = grant.RequiresSelection ? "领取所选奖励" : "领取奖励", AutoSize = true };
                claim.Click += async (_, _) =>
                {
                    if (_busy) return; _busy = true; claim.Enabled = false;
                    try { var result = await _features.ClaimRewardAsync(grant.GrantId, checks.Where(c => c.Box.Checked).Select(c => c.Id).ToArray(), _requests.Token); if (!IsDisposed) _status.Text = result.Message; }
                    catch (OperationCanceledException) { }
                    catch (Exception e) { if (!IsDisposed) _status.Text = e.Message; }
                    finally { _busy = false; if (!claim.IsDisposed) claim.Enabled = true; }
                    if (!IsDisposed) { string previous = _status.Text ?? ""; await RefreshAsync(); if (!IsDisposed) _status.Text = previous; }
                };
                group.Controls.Add(claim); _content.Controls.Add(group);
            }
            foreach (var source in scan.Sources)
            {
                string[] lines = source.Message.Split('\n', 2);
                _content.Controls.Add(new AntdUI.Label { Text = $"{source.Source}：{lines[0]}", Width = 650, Height = source.Source == "好运魄罗" ? 100 : 52 });
                if (lines.Length > 1)
                {
                    var detail = new AntdUI.Input { Text = lines[1].Replace("\n", Environment.NewLine), Width = 650, Height = 260,
                        Multiline = true, ReadOnly = true, AutoScroll = true, Visible = false };
                    string expand = source.Source == "好运魄罗" ? "查看礼包状态" : "查看每级奖励";
                    var toggle = new AntdUI.Button { Text = expand, AutoSize = true };
                    toggle.Click += (_, _) => { detail.Visible = !detail.Visible; toggle.Text = detail.Visible ? "收起奖励明细" : expand; };
                    _content.Controls.Add(toggle); _content.Controls.Add(detail);
                }
            }
            _status.Text = grants.Count == 0 ? "暂无已确认可领取的奖励；各来源扫描状态见上方" : $"{grants.Count} 组可领取奖励，多选项目请自行选择"; UiTheme.Apply(_content);
            AntdContentTheme.Apply(_content, UiTheme.Palette);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!IsDisposed) _status.Text = e.Message; }
        finally { _busy = false; }
    }
    public void ApplyTheme(ThemePalette palette) => AntdContentTheme.Apply(this, palette);
}
