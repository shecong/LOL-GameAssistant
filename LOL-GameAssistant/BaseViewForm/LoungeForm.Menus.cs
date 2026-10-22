using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public sealed partial class LoungeForm
{
    private void OpenLoungeMenu(Control owner, Point location, params (string Text, Action Invoke, bool Enabled)[] actions)
    {
        var items = actions.Select(action => (AntdUI.IContextMenuStripItem)new AntdUI.ContextMenuStripItem(UiLanguage.T(action.Text))
        { Tag = action.Invoke, Enabled = action.Enabled }).ToArray();
        AntdUI.ContextMenuStrip.open(new AntdUI.ContextMenuStrip.Config(owner, item =>
        {
            if (IsDisposed || item.Tag is not Action invoke) return;
            if (InvokeRequired) BeginInvoke(() => { if (!IsDisposed) invoke(); });
            else invoke();
        }, items)
        {
            Location = location, Font = Font,
            ColorScheme = UiTheme.Palette.IsDark ? AntdUI.TAMode.Dark : AntdUI.TAMode.Light
        });
    }

    private void ShowHistoryMenu(Point location)
    {
        var message = _senders.LastOrDefault(sender => sender.Start <= _history.SelectionStart).Message;
        OpenLoungeMenu(_history, location,
            ("屏蔽此消息发送者", () =>
            {
                try { _client.BlockSender(message!.SenderId); _historyKey = "\0"; RefreshView(); }
                catch (Exception ex) { _feedback.Text = ex.Message; }
            }, !string.IsNullOrEmpty(message?.SenderId)),
            ("复制玩家完整游戏 ID", () => TryCopyText(message!.Name), message != null),
            ("@此玩家", () =>
            {
                var member = _client.Members.FirstOrDefault(member => member.Name == message!.Name && member.Region == message.Region);
                if (member != null) Mention(member); else _feedback.Text = "该玩家不在线，暂时无法提及";
            }, message != null),
            ("查看开黑招募 / 加入", () =>
            {
                using var dialog = CreateRecruitmentDetails(message!.Id);
                dialog.ShowDialog(FindForm());
            }, message?.Kind == "recruit"));
    }

    private void ShowMemberMenu(Point location)
    {
        if (_members.SelectedIndex < 0 || _members.SelectedIndex >= _visibleMembers.Length) return;
        var member = _visibleMembers[_members.SelectedIndex];
        var actions = new List<(string, Action, bool)>();
        foreach (string field in new[] { "名称", "编号", "完整游戏ID", "频道标识" })
            actions.Add(("复制" + field, () => TryCopyText(MemberCopyText(member, field)), true));
        actions.Add(("@此成员", () => Mention(member), true));
        actions.Add((_socialExperience.FavoriteTeammates.ContainsKey(member.Id) ? "取消常用队友" : "收藏为常用队友", () => { _ = ToggleFavoriteAsync(member); }, true));
        actions.Add((_client.IsFollowed(member.Id) ? "取消关注" : "关注上线提醒", () =>
        {
            try { _client.ToggleFollow(member.Id); _memberKey = ""; RefreshView(); }
            catch (Exception ex) { _feedback.Text = ex.Message; }
        }, true));
        OpenLoungeMenu(_members, location, actions.ToArray());
    }
}
