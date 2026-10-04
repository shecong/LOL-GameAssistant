using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public partial class SettingForm
{
    private readonly IClientFeatureService _matchFeatures;
    private readonly AntdUI.InputNumber _acceptMin = new() { Minimum = 0, Maximum = 15000, Width = 110, Height = 32 };
    private readonly AntdUI.InputNumber _acceptMax = new() { Minimum = 0, Maximum = 15000, Width = 110, Height = 32 };
    private readonly AntdUI.Switch _preselectOnly = new();
    private readonly AntdUI.Switch _skipFill = new();
    private readonly AntdUI.Switch _autoHonor = new();
    private readonly AntdUI.Switch _autoReturn = new();
    private readonly AntdUI.Switch _returnAndSearch = new();
    private readonly AntdUI.InputNumber _quickQueue = new() { Minimum = 1, Maximum = 3000, Value = 430, Height = 32 };
    private readonly AntdUI.Label _matchToolsStatus = new() { AutoSize = true, Text = "操作仅作用于当前登录的客户端。" };

    private Control CreateAcceptDelayRow()
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
        row.Controls.Add(_acceptMin);
        row.Controls.Add(new AntdUI.Label { AutoSize = true, Text = "至", Padding = new Padding(0, 7, 0, 0) });
        row.Controls.Add(_acceptMax);
        return row;
    }

    private Control CreateMatchActions()
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
        void Add(string text, string tip, Func<Task<ClientFeatureResult>> action)
        {
            var button = new AntdUI.Button { Text = text, AutoSize = true, Height = 34, Margin = new Padding(3) };
            AttachTipDeep(button, tip);
            button.Click += async (_, _) =>
            {
                row.Enabled = false;
                _matchToolsStatus.Text = UiLanguage.T("正在执行…");
                try
                {
                    ClientFeatureResult result = await action();
                    if (!IsDisposed) _matchToolsStatus.Text = UiLanguage.T(result.Message);
                }
                catch (Exception ex)
                {
                    if (!IsDisposed) _matchToolsStatus.Text = UiLanguage.T($"操作失败：{ex.Message}");
                }
                finally { if (!row.IsDisposed) row.Enabled = true; }
            };
            row.Controls.Add(button);
        }
        Add("快速创建大厅", "使用上方队列 ID 调用本机客户端创建大厅。", () => _matchFeatures.CreateQuickLobbyAsync((int)_quickQueue.Value));
        Add("取消当前匹配", "请求客户端取消当前匹配确认，不会影响已经开始的对局。", () => _matchFeatures.DeclineReadyCheckAsync());
        Add("退出英雄选择", "请求退出当前英雄选择；仅客户端允许退出的阶段会成功。", () => _matchFeatures.DodgeChampionSelectAsync());
        return row;
    }

    private void LoadMatchToolsSettings()
    {
        _acceptMin.Value = Math.Clamp(_config.AutoAcceptDelayMinMilliseconds, 0, 15000);
        _acceptMax.Value = Math.Clamp(_config.AutoAcceptDelayMaxMilliseconds, 0, 15000);
        _preselectOnly.Checked = _config.AutoPickPreselectOnly;
        _skipFill.Checked = _config.SkipAutoPickOnFill;
        _autoHonor.Checked = _config.AutoHonor;
        _autoReturn.Checked = _config.AutoReturnToLobby;
        _returnAndSearch.Checked = _config.AutoReturnStartMatchmaking;
        _quickQueue.Value = Math.Clamp(_config.QuickLobbyQueueId, 1, 3000);
        foreach (AntdUI.Switch toggle in new[] { _preselectOnly, _skipFill, _autoHonor, _autoReturn, _returnAndSearch })
            toggle.CheckedChanged += (_, _) => SaveSettings();
        _acceptMin.ValueChanged += (_, _) => SaveSettings();
        _acceptMax.ValueChanged += (_, _) => SaveSettings();
        _quickQueue.ValueChanged += (_, _) => SaveSettings();
    }

    private void ReadMatchToolsSettings()
    {
        // Treat either edit order as a valid range, without clearing the user's delay values.
        _config.AutoAcceptDelayMinMilliseconds = (int)Math.Min(_acceptMin.Value, _acceptMax.Value);
        _config.AutoAcceptDelayMaxMilliseconds = (int)Math.Max(_acceptMin.Value, _acceptMax.Value);
        _config.AutoPickPreselectOnly = _preselectOnly.Checked;
        _config.SkipAutoPickOnFill = _skipFill.Checked;
        _config.AutoHonor = _autoHonor.Checked;
        _config.AutoReturnToLobby = _autoReturn.Checked;
        _config.AutoReturnStartMatchmaking = _returnAndSearch.Checked;
        _config.QuickLobbyQueueId = (int)_quickQueue.Value;
    }
}
