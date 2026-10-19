using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

internal sealed class RewardSelectionForm : AntdUI.Window
{
    private readonly IClientFeatureService _features;
    private readonly UiRequestScope _requests = new();
    private readonly FlowLayoutPanel _content = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16) };
    private readonly AntdUI.Label _status = new() { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(16) };
    private bool _busy;
    internal RewardSelectionForm(IClientFeatureService features)
    {
        _features = features; Text = "待选奖励 · 先预览再领取"; Size = new Size(760, 620); MinimumSize = new Size(600, 450); StartPosition = FormStartPosition.CenterParent;
        var refresh = new AntdUI.Button { Text = "刷新奖励", Dock = DockStyle.Top, Height = 45 }; refresh.Click += async (_, _) => await RefreshAsync();
        Controls.Add(_content); Controls.Add(_status); Controls.Add(refresh); AntdWindowChrome.AddHeader(this);
        Shown += async (_, _) => await RefreshAsync(); Disposed += (_, _) => _requests.Dispose(); UiTheme.Apply(this);
    }
    private async Task RefreshAsync()
    {
        if (_busy || IsDisposed) return; _busy = true;
        try
        {
            var grants = await _features.GetPendingRewardsAsync(_requests.Token); if (IsDisposed) return;
            ControlLifetime.ClearAndDispose(_content);
            foreach (var grant in grants)
            {
                var group = new FlowLayoutPanel { Width = 650, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12), Margin = new Padding(0, 0, 0, 15) };
                group.Controls.Add(new AntdUI.Label { Text = $"{grant.Title} · 请选择 {grant.MinimumSelections}～{grant.AvailableChoices} 项", AutoSize = true });
                var checks = new List<(AntdUI.Checkbox Box, string Id)>();
                foreach (var choice in grant.Choices.Count > 0 ? grant.Choices : grant.RewardIds.Select(id => new ClientRewardChoice(id, id, "未提供名称与说明")).ToArray())
                {
                    var box = new AntdUI.Checkbox { Text = $"{choice.Name} · {choice.Description}", Width = 610, Height = 48, Checked = grant.RewardIds.Count == 1 };
                    checks.Add((box, choice.Id)); group.Controls.Add(box);
                }
                var claim = new AntdUI.Button { Text = "领取所选奖励", AutoSize = true };
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
            _status.Text = grants.Count == 0 ? "未取得待选奖励；可能暂无奖励或客户端接口暂不可用" : $"{grants.Count} 组待选奖励，多选项目不会默认选中"; UiTheme.Apply(_content);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!IsDisposed) _status.Text = e.Message; }
        finally { _busy = false; }
    }
}
