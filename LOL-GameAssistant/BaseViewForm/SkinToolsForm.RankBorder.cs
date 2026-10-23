using LOL_GameAssistant.Domain.Skins;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public sealed partial class SkinToolsForm
{
    private readonly AntdUI.Button _rankBorderTab = new() { Name = "RankBorderTab", Text = "段位边框", Width = 140, Height = 48,
        Font = new Font(UiMetrics.FontFamily, 11F, FontStyle.Bold), Margin = new Padding(0, 0, 18, 0), BorderWidth = 1, Radius = 8 };
    private readonly AntdUI.Select _rankBorders = new() { Name = "RankBorderEntries", Dock = DockStyle.Top, Height = 56,
        List = true, MaxCount = 12, DropDownArrow = true, PlaceholderText = "读取兼容的边框列表后选择", Enabled = false };
    private readonly AntdUI.Button _borderRead = new() { Name = "RankBorderRead", Text = "读取边框状态", Width = 146, Height = 42 };
    private readonly AntdUI.Button _borderApply = new() { Name = "RankBorderApply", Text = "应用边框", Width = 146, Height = 42 };
    private readonly AntdUI.Button _borderRestore = new() { Name = "RankBorderRestore", Text = "恢复原边框", Width = 146, Height = 42 };
    private readonly AntdUI.Label _borderStatus = new() { Name = "RankBorderStatus", Dock = DockStyle.Top, Height = 100,
        TextMultiLine = true, Text = "待适配：当前随附核心尚未提供经过版本验证的段位边框接口。" };
    private AntdUI.Panel? _borderCard;
    private SkinCoreReply? _borderCatalog;

    private Control CreateRankBorderPage()
    {
        var page = new Panel { Name = "RankBorderPage", Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12), Visible = false };
        _borderCard = new AntdUI.Panel { Dock = DockStyle.Top, Height = 430, Radius = 14, Padding = new Padding(24), BorderWidth = 1 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 110, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(0, 16, 0, 12) };
        foreach (var button in new[] { _borderRead, _borderApply, _borderRestore }) button.Margin = new Padding(0, 0, 12, 12);
        buttons.Controls.AddRange([_borderRead, _borderApply, _borderRestore]);
        _borderCard.Controls.Add(_borderStatus);
        _borderCard.Controls.Add(buttons);
        _borderCard.Controls.Add(_rankBorders);
        _borderCard.Controls.Add(new AntdUI.Label { Dock = DockStyle.Top, Height = 80, TextMultiLine = true,
            Text = "读取当前对局支持的边框后，选择并点击应用。边框仅影响本地外观，不改变账号段位；暂不支持的版本会显示待适配。" });
        _borderCard.Controls.Add(new AntdUI.Label { Dock = DockStyle.Top, Height = 48, Text = "段位边框",
            Font = new Font(UiMetrics.FontFamily, 16, FontStyle.Bold) });
        page.Controls.Add(_borderCard);
        _surfaceRows.Add(buttons); _pageSurfaces.Add(page);
        _borderRead.Click += async (_, _) => await RunRankBorderAsync(0);
        _borderApply.Click += async (_, _) => await RunRankBorderAsync(1);
        _borderRestore.Click += async (_, _) => await RunRankBorderAsync(2);
        _rankBorders.SelectedIndexChanged += (_, _) => UpdateRankBorderButtons();
        return page;
    }

    private void UpdateRankBorderButtons()
    {
        bool ready = !_disposing && _inProgress && _core.Enabled && !_busy && !_refreshing && _pageRequests != null;
        bool connected = ready && _borderCatalog != null;
        _borderRead.Enabled = ready;
        _rankBorders.Enabled = connected;
        _borderApply.Enabled = connected && _rankBorders.SelectedIndex >= 0;
        _borderRestore.Enabled = connected;
    }

    private void ResetRankBorder()
    {
        _borderCatalog = null; _rankBorders.Items.Clear(); _rankBorders.SelectedIndex = -1;
        _borderStatus.Text = UiLanguage.T("当前会话已失效，请在对局中重新读取边框状态。当前随附核心尚未适配段位边框。");
    }

    private async Task RunRankBorderAsync(int action)
    {
        if (_disposing || !_inProgress || !_core.Enabled || _busy || _refreshing || _pageRequests == null) return;
        var catalog = _borderCatalog;
        if (action != 0 && catalog == null) return;
        string? id = _rankBorders.SelectedValue is string selected ? selected : null;
        if (action == 1 && id == null) return;
        var token = _pageRequests.Token;
        long revision = Interlocked.Read(ref _revision);
        _busy = true; UpdateButtons();
        _borderStatus.Text = UiLanguage.T(action == 0 ? "正在读取边框能力…" : "正在处理边框请求…");
        try
        {
            var reply = action switch {
                0 => await _core.GetRankBorderAsync(token),
                1 => await _core.ApplyRankBorderAsync(catalog!.Session, catalog.RankBorderProfile, id!, token),
                _ => await _core.RestoreRankBorderAsync(catalog!.Session, catalog.RankBorderProfile, token)
            };
            if (_disposing || IsDisposed || token.IsCancellationRequested || revision != Interlocked.Read(ref _revision)) return;
            if (action == 0)
            {
                _borderCatalog = reply; _rankBorders.Items.Clear();
                foreach (var entry in reply.RankBorderEntries) _rankBorders.Items.Add(new AntdUI.SelectItem(entry.Name, entry.Id));
                _rankBorders.SelectedValue = reply.RankBorderId;
                _borderStatus.Text = UiLanguage.T("边框列表已读取。选择后点击应用；应用与恢复均需状态核验。");
            }
            else
            {
                _borderStatus.Text = UiLanguage.T(reply.Invoked && reply.StateVerified
                    ? action == 2 ? "原边框已恢复，状态核验通过；画面效果仍需确认。" : "边框调用完成，状态核验通过；画面效果仍需确认。"
                    : "边框结果未核验，不视为成功。请重新读取状态。");
                if (!reply.StateVerified) { _borderCatalog = null; _rankBorders.Items.Clear(); }
            }
        }
        catch (Exception error)
        {
            if (_disposing || IsDisposed || token.IsCancellationRequested || revision != Interlocked.Read(ref _revision)) return;
            _borderCatalog = null; _rankBorders.Items.Clear();
            _borderStatus.Text = UiLanguage.T(error is SkinCoreException { Code: "rank_border_not_supported" }
                ? "待适配：当前核心不支持段位边框，未执行边框修改。"
                : error is SkinCoreException { OutcomeUnknown: true }
                    ? "边框请求结果未知，不视为成功。请重新读取状态或退出对局。"
                    : $"边框操作未完成：{error.Message}");
        }
        finally { _busy = false; if (!_disposing && !IsDisposed) UpdateButtons(); }
    }
}
