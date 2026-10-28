using Panel = AntdUI.Panel;
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
        TextMultiLine = true, Text = "待适配：当前版本暂不支持加载画面的任意段位边框。" };
    private AntdUI.Panel? _borderCard;
    private SkinCoreReply? _borderCatalog;
    private readonly CancellationTokenSource _borderLifetime = new();
    private readonly AntdUI.Checkbox _borderAutomatic = new() { Name = "RankBorderAutomatic", Text = "每局加载时自动应用所选边框", Dock = DockStyle.Top, Height = 40 };
    private bool _borderPreferenceUpdating;

    private Control CreateRankBorderPage()
    {
        var page = new Panel { Name = "RankBorderPage", Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12), Visible = false };
        _borderCard = new AntdUI.Panel { Dock = DockStyle.Top, Height = 560, Radius = 14, Padding = new Padding(24), BorderWidth = 1 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 110, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(0, 16, 0, 12) };
        foreach (var button in new[] { _borderRead, _borderApply, _borderRestore }) button.Margin = new Padding(0, 0, 12, 12);
        buttons.Controls.AddRange([_borderRead, _borderApply, _borderRestore]);
        _borderCard.Controls.Add(_borderStatus);
        _borderCard.Controls.Add(buttons);
        _borderCard.Controls.Add(_borderAutomatic);
        _borderCard.Controls.Add(_rankBorders);
        _borderCard.Controls.Add(new AntdUI.Label { Dock = DockStyle.Top, Height = 80, TextMultiLine = true,
            Text = "选择段位并开启每局自动应用，在下一局加载时改变自己的卡片外观，不改变真实段位。当前适配 16.21，加载时会再次核对版本与资源。进入对局后可恢复本局原边框。" });
        _borderCard.Controls.Add(new AntdUI.Label { Dock = DockStyle.Top, Height = 48, Text = "加载画面段位边框",
            Font = new Font(UiMetrics.FontFamily, 16, FontStyle.Bold) });
        page.Controls.Add(_borderCard);
        page.Controls.Add(CreateClientBorderCard());
        _surfaceRows.Add(buttons); _pageSurfaces.Add(page);
        _borderRead.Click += async (_, _) => await RunRankBorderAsync(0);
        _borderApply.Click += async (_, _) => await RunRankBorderAsync(1);
        _borderRestore.Click += async (_, _) => await RunRankBorderAsync(2);
        PopulateBorderPresets();
        _rankBorders.SelectedIndexChanged += (_, _) => { UpdateRankBorderButtons();if (_borderAutomatic.Checked) SaveBorderPreference(); };
        _borderAutomatic.CheckedChanged += (_, _) => SaveBorderPreference();
        return page;
    }

    private void UpdateRankBorderButtons()
    {
        bool ready = !_disposing && _core.Enabled && !_busy && !_refreshing;
        bool connected = ready && _borderCatalog != null;
        _borderRead.Enabled = ready;
        _rankBorders.Enabled = !_disposing && !_busy;
        _borderApply.Enabled = connected && _borderCatalog!.RankBorderLoadingReady && _rankBorders.SelectedIndex >= 0;
        _borderRestore.Enabled = connected && _borderCatalog!.RankBorderBaselineAvailable;
        _borderAutomatic.Enabled = !_disposing && !_busy;
    }

    private void ResetRankBorder()
    {
        _borderCatalog = null;PopulateBorderPresets();
        _borderStatus.Text = UiLanguage.T("选择段位并开启每局自动应用，在下一局加载时生效。读取状态可查看本局外观；加载结束后不再强行切换。");
    }

    private async Task RunRankBorderAsync(int action)
    {
        if (_disposing || !_core.Enabled || _busy || _refreshing) return;
        var catalog = _borderCatalog;
        if (action != 0 && catalog == null) return;
        string? id = _rankBorders.SelectedValue is string selected ? selected : null;
        if (action == 1 && id == null) return;
        var token = _borderLifetime.Token;
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
                _borderPreferenceUpdating = true;
                try
                {
                    _borderCatalog = reply;_rankBorders.Items.Clear();
                    foreach (var entry in reply.RankBorderEntries) _rankBorders.Items.Add(new AntdUI.SelectItem(entry.Name, entry.Id));
                    var preference = _settings.Load();
                    _rankBorders.SelectedValue = preference.RankBorderAutoApplyEnabled && RankBorderPresets.IsValid(preference.PreferredRankBorderId)
                        ? preference.PreferredRankBorderId : reply.RankBorderId;
                }
                finally { _borderPreferenceUpdating = false; }
                _borderStatus.Text = UiLanguage.T(reply.RankBorderLoadingReady ? "当前加载资源已就绪，可应用边框。" : "加载阶段已结束。可保存下一局的选择；本局已修改的边框仍可恢复。");
            }
            else
            {
                _borderStatus.Text = UiLanguage.T(reply.Invoked && reply.StateVerified
                    ? action == 2 ? "原边框已恢复，状态核验通过；画面效果仍需确认。" : "边框调用完成，状态核验通过；画面效果仍需确认。"
                    : "边框结果未核验，不视为成功。请重新读取状态。");
                if (reply.StateVerified) _borderCatalog = reply;
                else { _borderCatalog = null;PopulateBorderPresets(); }
            }
        }
        catch (Exception error)
        {
            if (_disposing || IsDisposed || token.IsCancellationRequested || revision != Interlocked.Read(ref _revision)) return;
            _borderCatalog = null;PopulateBorderPresets();
            _borderStatus.Text = UiLanguage.T(error is SkinCoreException { Code: "rank_border_not_supported" }
                ? "待适配：当前核心不支持段位边框，未执行边框修改。"
                : error is SkinCoreException { OutcomeUnknown: true }
                    ? "边框请求结果未知，不视为成功。请重新读取状态或退出对局。"
                    : $"边框操作未完成：{error.Message}");
        }
        finally { _busy = false; if (!_disposing && !IsDisposed) UpdateButtons(); }
    }

    private void PopulateBorderPresets()
    {
        _borderPreferenceUpdating = true;
        try
        {
            var settings = _settings.Load();_rankBorders.Items.Clear();
            foreach (var entry in RankBorderPresets.Entries) _rankBorders.Items.Add(new AntdUI.SelectItem(entry.Name, entry.Id));
            _rankBorders.SelectedValue = RankBorderPresets.IsValid(settings.PreferredRankBorderId) ? settings.PreferredRankBorderId : null;
            _borderAutomatic.Checked = settings.RankBorderAutoApplyEnabled;
        }
        finally { _borderPreferenceUpdating = false; }
    }
    private void SaveBorderPreference()
    {
        if (_borderPreferenceUpdating || _disposing) return;
        string? id = _rankBorders.SelectedValue as string;
        if (_borderAutomatic.Checked && !RankBorderPresets.IsValid(id))
        {
            _borderPreferenceUpdating = true;try { _borderAutomatic.Checked = false; } finally { _borderPreferenceUpdating = false; }
            _borderStatus.Text = "请先选择加载画面要使用的段位边框。";return;
        }
        try
        {
            var settings = _settings.Load();if (RankBorderPresets.IsValid(id)) settings.PreferredRankBorderId = id!;settings.PreferredRankBorderProfile = RankBorderPresets.Profile;
            settings.RankBorderAutoApplyEnabled = _borderAutomatic.Checked;if (_borderAutomatic.Checked) settings.SkinCoreEnabled = true;
            _settings.Save(settings);
            if (_borderAutomatic.Checked && !_enabled.Checked) _enabled.Checked = true;
            _borderStatus.Text = _borderAutomatic.Checked ? "已保存，下一局加载时自动应用。实际画面以加载卡片为准。" : "自动应用已关闭，当前外观不会自动恢复。";
        }
        catch (Exception error) { _borderStatus.Text = "边框偏好保存失败：" + error.Message; }
    }
}
