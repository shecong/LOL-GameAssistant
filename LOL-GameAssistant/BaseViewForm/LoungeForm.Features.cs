using LOL.Lounge;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.Lounge;

namespace LOL_GameAssistant.BaseViewForm;

public sealed partial class LoungeForm
{
    private readonly Dictionary<string, string> _mentionTargets = [];
    private string? _pendingSticker;
    private readonly Label _unread = new() { AutoSize = true, Padding = new Padding(8, 6, 0, 0) };
    private readonly CheckBox _dnd = new() { AutoSize = true, Text = "免打扰" };
    private readonly CheckBox _quiet = new() { AutoSize = true, Text = "游戏中静音" };
    private readonly CheckBox _pentakill = new() { AutoSize = true, Text = "播报五杀" };
    private readonly CheckBox _legendary = new() { AutoSize = true, Text = "播报超神" };
    private readonly CheckBox _promotion = new() { AutoSize = true, Text = "播报晋级" };
    private readonly Button _stickerButton = new() { Text = "表情包", AutoSize = true };
    private readonly Button _recruitButton = new() { Text = "开黑招募", AutoSize = true };
    private readonly Button _recruitListButton = new() { Text = "招募列表", AutoSize = true };
    private readonly Button _mentionButton = new() { Text = "@成员", AutoSize = true };
    private readonly List<Button> _featureButtons = [];

    private Control BuildPreferences()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
        var settings = _client.Settings;
        _dnd.Checked = settings.DoNotDisturb;
        _quiet.Checked = settings.QuietInGame;
        _pentakill.Checked = settings.AnnouncePentakills;
        _legendary.Checked = settings.AnnounceLegendary;
        _promotion.Checked = settings.AnnouncePromotions;
        bar.Controls.AddRange([_dnd, _quiet, _pentakill, _legendary, _promotion]);
        foreach (var checkbox in new[] { _dnd, _quiet, _pentakill, _legendary, _promotion })
            checkbox.CheckedChanged += (_, _) =>
            {
                try
                {
                    _client.SetPreferences(_dnd.Checked, _quiet.Checked, _pentakill.Checked, _legendary.Checked, _promotion.Checked);
                    _feedback.Text = "提醒与播报设置已保存";
                }
                catch (Exception ex) { _feedback.Text = ex.Message; }
            };
        return bar;
    }

    private Control BuildFeatureBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
        _featureButtons.AddRange([_stickerButton, _recruitButton, _recruitListButton, _mentionButton]);
        var more = new Button { Text = "更多 ▾", AutoSize = true };
        _featureButtons.Add(more);
        _moreMenu.Items.Add("清除表情和提及", null, (_, _) => { ClearSticker(); _mentionTargets.Clear(); _feedback.Text = "已清除表情和提及"; });
        _moreMenu.Items.Add("标为已读", null, (_, _) => { _client.SetReading(true); _client.SetReading(false); UpdateFeatureBar(); });
        more.Click += (_, _) => _moreMenu.Show(more, new Point(0, more.Height));
        bar.Controls.AddRange([_stickerButton, _mentionButton, _recruitButton, _recruitListButton, more, _unread]);
        _stickerButton.Click += (_, _) => ShowStickerPicker();
        _recruitButton.Click += (_, _) => ShowRecruitment();
        _recruitListButton.Click += (_, _) => ShowRecruitmentList();
        _mentionButton.Click += (_, _) => ShowMemberPicker();
        return bar;
    }

    private void UpdateFeatureBar()
    {
        _stickerButton.Enabled = _mentionButton.Enabled = !_sending && _client.CanSend && _client.SupportsRichMessages;
        _recruitButton.Enabled = !_sending && _client.CanSend;
        _unread.Text = $"未读 {_client.UnreadCount} · @我 {_client.MentionCount}" + (_client.NotificationsMuted ? " · 已静音" : "");
    }

    private void WithMember(Action<LoungeMember> action)
    {
        int index = _members.SelectedIndex;
        if (index >= 0 && index < _visibleMembers.Length) action(_visibleMembers[index]);
    }

    private void TryCopyText(string text)
    {
        try { Clipboard.SetText(text); _feedback.Text = "已复制游戏 ID"; }
        catch (System.Runtime.InteropServices.ExternalException) { _feedback.Text = "剪贴板忙，请重试"; }
    }

    private void Mention(LoungeMember member)
    {
        if (!_client.CanSend) { _feedback.Text = "请先连接频道并登录 LOL"; return; }
        if (member.Id == _client.CurrentPlayerId) { _feedback.Text = "无需提及自己"; return; }
        if (_mentionTargets.Count >= 5 && !_mentionTargets.ContainsKey(member.Id))
        { _feedback.Text = "每条消息最多提及 5 位成员"; return; }
        string mention = "@" + member.Name + " ";
        if (_pendingSticker == null && _input.TextLength + mention.Length > LoungeRules.MaxTextLength)
        { _feedback.Text = "消息已达到字数限制"; return; }
        if (_pendingSticker != null) ClearSticker();
        _mentionTargets[member.Id] = member.Name;
        _input.AppendText(mention);
        _input.Focus();
    }

    private void ShowMemberPicker()
    {
        using var dialog = new Form { Text = "选择要提及的成员", ClientSize = new Size(460, 340), StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false };
        var list = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true };
        var members = _client.Members.Where(m => m.Id != _client.CurrentPlayerId).ToArray();
        list.Items.AddRange(members.Select(m => (object)$"{m.Name} · {m.Region}").ToArray());
        var choose = new Button { Text = "提及此成员", Dock = DockStyle.Bottom, Height = 36 };
        choose.Click += (_, _) => { if (list.SelectedIndex >= 0) { Mention(members[list.SelectedIndex]); dialog.Close(); } };
        list.DoubleClick += (_, _) => choose.PerformClick();
        dialog.Controls.Add(list); dialog.Controls.Add(choose);
        UiTheme.Apply(dialog);
        dialog.ShowDialog(FindForm());
    }

    private void ClearSticker()
    {
        _pendingSticker = null;
        _input.ReadOnly = false;
        _input.Clear();
    }

    private void ShowStickerPicker()
    {
        using var dialog = new Form { Text = "内置表情包 · 选择后点击发送", ClientSize = new Size(680, 310), BackColor = UiTheme.Palette.Surface,
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var grid = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4), AutoScroll = true };
        var images = new List<Bitmap>();
        try
        {
            foreach (var sticker in LoungeContent.Stickers)
            {
                var image = LoungeStickerArt.Create(sticker);
                images.Add(image);
                var button = new Button { Image = image, Size = new Size(160, 140), AccessibleName = sticker.Caption,
                    FlatStyle = FlatStyle.Flat, BackColor = ColorTranslator.FromHtml(sticker.Color) };
                button.Click += (_, _) =>
                {
                    _mentionTargets.Clear();
                    _pendingSticker = sticker.Id;
                    _input.ReadOnly = true;
                    _input.Text = "[表情包：" + sticker.Caption + "]";
                    _feedback.Text = "已选择表情包，点击发送";
                    dialog.Close();
                };
                grid.Controls.Add(button);
            }
            dialog.Controls.Add(grid);
            dialog.ShowDialog(FindForm());
        }
        finally { foreach (var image in images) image.Dispose(); }
    }

}
