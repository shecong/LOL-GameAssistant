using LOL.Lounge;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.Lounge;
using LOL_GameAssistant.Domain.MatchAnalysis;

namespace LOL_GameAssistant.BaseViewForm;

public sealed partial class LoungeForm
{
    private void ShowRecruitment()
    {
        using var dialog = CreateRecruitmentDialog();
        dialog.ShowDialog(FindForm());
    }

    internal Form CreateRecruitmentDialog()
    {
        string openedAccount = _client.CurrentPlayerId;
        var dialog = RecruitmentDialog("发布开黑招募", new Size(540, 520));
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 9, Padding = new Padding(16) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 5; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        AntdUI.Select Dropdown(string name, IEnumerable<string> choices)
        {
            var box = new AntdUI.Select { Name = name, Dock = DockStyle.Fill, List = true, DropDownArrow = true };
            box.Items.AddRange(choices.Cast<object>().ToArray()); box.SelectedIndex = 0; return box;
        }
        var regions = Enumerable.Range(1, 19).Select(i => LoungeLcuReader.RegionName("HN" + i))
            .Concat(Enumerable.Range(1, 7).Select(i => LoungeLcuReader.RegionName("WT" + i)))
            .Concat(["男爵领域", "峡谷之巅", "韩服", "北美", "欧洲西部", "日服"])
            .Prepend(_client.CurrentRegion).Where(r => !string.IsNullOrWhiteSpace(r) && !r.Contains("未知")).Distinct();
        var region = Dropdown("Region", regions);
        var modes = LoungeRules.Modes.ToArray();
        var mode = Dropdown("Mode", modes.Select(m => m.Value + "（" + m.Key + "）"));
        var rank = Dropdown("Rank", LoungeContent.Ranks);
        var position = Dropdown("Position", LoungeContent.Positions);
        var needed = new AntdUI.InputNumber { Name = "Needed", Dock = DockStyle.Fill, Minimum = 1, Maximum = 4, Value = 1 };
        var note = new AntdUI.Input { Name = "Note", Dock = DockStyle.Fill, MaxLength = 120, Multiline = true,
            PlaceholderText = "偏好标签与说明合计最多120字，例如：友好交流" };
        var hint = new AntdUI.Label { Dock = DockStyle.Fill, Text = $"{_socialExperience.PlayStyle} · {(_socialExperience.Voice ? "愿意语音" : "无需语音")}；可在休息室更多菜单修改偏好。30分钟后到期。", TextMultiLine = true, TextAlign = ContentAlignment.MiddleLeft };
        var error = new AntdUI.Label { Name = "Feedback", Dock = DockStyle.Fill, TextMultiLine = true, AutoEllipsis = true };
        var publish = new AntdUI.Button { Name = "Publish", Text = "发布到公频", Dock = DockStyle.Fill };
        Control[] fields = [region, mode, rank, position, needed, note];
        string id = Guid.NewGuid().ToString("N");
        string? payloadKey = null;
        bool sending = false;
        dialog.FormClosing += (_, e) => { if (sending) { e.Cancel = true; error.Text = "正在确认发布结果，请稍候…"; } };
        publish.Click += async (_, _) =>
        {
            if (sending) return;
            if (openedAccount.Length > 0 && openedAccount != _client.CurrentPlayerId)
            { error.Text = "账号已切换，请关闭后重新发布招募。"; return; }
            sending = true;
            publish.Enabled = false;
            publish.Text = "正在发布…";
            error.Text = "正在确认发布结果…";
            foreach (var control in fields) control.Enabled = false;
            try
            {
                var recruitment = new LoungeRecruitment(region.Text, modes[mode.SelectedIndex].Key, rank.Text,
                    position.Text, (int)needed.Value, RecruitmentPreferences.Compose(_socialExperience.PlayStyle, _socialExperience.Voice, _socialExperience.AvailableTime, note.Text ?? ""));
                string nextKey = LoungeContent.Serialize(recruitment);
                if (payloadKey != null && payloadKey != nextKey) id = Guid.NewGuid().ToString("N");
                payloadKey = nextKey;
                await _client.SendPostAsync(new(id, "", "recruit", Recruitment: recruitment));
                sending = false;
                if (!dialog.IsDisposed)
                {
                    _feedback.Text = "开黑招募已发布，可在招募列表查看报名或结束招募";
                    RefreshView();
                    dialog.DialogResult = DialogResult.OK;
                    dialog.Close();
                }
            }
            catch (Exception ex) { if (!dialog.IsDisposed) error.Text = LoungeClient.SendError(ex); }
            finally
            {
                sending = false;
                if (!dialog.IsDisposed)
                {
                    publish.Enabled = true; publish.Text = "发布到公频";
                    foreach (var control in fields) control.Enabled = true;
                }
            }
        };
        int row = 0;
        foreach (var (label, control) in new (string, Control)[] { ("大区", region), ("模式", mode), ("段位要求", rank),
            ("缺少位置", position), ("缺少人数", needed), ("补充说明", note), ("", hint), ("", error), ("", publish) })
        { grid.Controls.Add(new AntdUI.Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row); grid.Controls.Add(control, 1, row++); }
        dialog.Controls.Add(grid);
        AntdWindowChrome.AddHeader(dialog); UiTheme.Apply(dialog);
        return dialog;
    }

    private AntdContentWindow RecruitmentDialog(string title, Size size) => new()
    {
        Text = title, ClientSize = size, Font = Font, StartPosition = FormStartPosition.CenterParent,
        FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
        BackColor = UiTheme.Palette.Surface, ForeColor = UiTheme.Palette.TextPrimary
    };

    internal static string RecruitmentSummary(LoungeRecruitmentState state)
    {
        var r = state.Recruitment;
        return $"🎮 开黑招募 · {state.Status} · {r.Region} · {LoungeRules.Modes.GetValueOrDefault(r.QueueId, "未知模式")}\n" +
            $"{r.Rank} · {r.Position} · 已报名 {state.Participants.Length}/{r.Needed} 人\n{r.Note}\n" +
            $"发起人：{state.OwnerName} · 到期 {state.ExpiresAt.ToLocalTime():HH:mm}\n双击查看详情、报名或管理招募。";
    }

    private long? SelectedRecruitmentId()
    {
        var message = _senders.LastOrDefault(s => s.Start <= _history.SelectionStart).Message;
        return message?.Kind == "recruit" ? message.Id : null;
    }

    private void OpenSelectedRecruitment()
    {
        if (SelectedRecruitmentId() is not { } id) return;
        using var dialog = CreateRecruitmentDetails(id);
        dialog.ShowDialog(FindForm());
    }

    private void ShowRecruitmentList()
    {
        using var dialog = CreateRecruitmentListDialog();
        dialog.ShowDialog(FindForm());
    }
    internal Form CreateRecruitmentListDialog()
    {
        var dialog = RecruitmentDialog("开黑招募列表 · 选择后查看详情", new Size(760, 480));
        var list = CreateLoungeList();
        list.Name = "recruitmentResults";
        var filters = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(8) };
        var sameRegion = new AntdUI.Checkbox { Name = "sameRegion", Text = "同大区", Checked = true, AutoSize = true };
        var active = new AntdUI.Checkbox { Name = "activeRecruitments", Text = "仅可加入", Checked = true, AutoSize = true };
        var style = new AntdUI.Select { Name = "recruitmentStyle", List = true, DropDownArrow = true, Width = 140, Height = 36 };
        style.Items.Add("全部目的"); style.Items.AddRange(RecruitmentPreferences.Styles.Cast<object>().ToArray()); style.SelectedIndex = 0;
        var position = new AntdUI.Select { Name = "recruitmentPosition", List = true, DropDownArrow = true, Width = 140, Height = 36 };
        position.Items.AddRange(LoungeContent.Positions.Cast<object>().ToArray()); position.SelectedIndex = 0;
        filters.Controls.AddRange([sameRegion, active, style, position]);
        var open = new AntdUI.Button { Text = "查看详情 / 加入 / 管理", Dock = DockStyle.Bottom, Height = 42 };
        var hint = new AntdUI.Label { Dock = DockStyle.Top, Height = 44, Text = "报名后复制发起人游戏 ID，在 LOL 客户端添加好友组队。段位及位置由双方确认。" };
        LoungeRecruitmentState[] states = [];
        string key = "\0";
        void RefreshList()
        {
            var blocked = _client.Settings.BlockedSenders ?? [];
            var deletedOrBlocked = _client.Messages.Where(m => m.Kind == "deleted" || blocked.Contains(m.SenderId)).Select(m => m.Id).ToHashSet();
            var next = _client.Recruitments.Where(r => !deletedOrBlocked.Contains(r.MessageId) &&
                (!sameRegion.Checked || string.IsNullOrWhiteSpace(_client.CurrentRegion) || r.Recruitment.Region == _client.CurrentRegion) &&
                (!active.Checked || (!r.Closed && r.ExpiresAt > DateTimeOffset.UtcNow && r.Participants.Length < r.Recruitment.Needed)) &&
                (style.SelectedIndex <= 0 || RecruitmentPreferences.Style(r.Recruitment.Note) == style.SelectedValue?.ToString()) &&
                (position.SelectedIndex <= 0 || r.Recruitment.Position == "任意位置" || r.Recruitment.Position == position.SelectedValue?.ToString()))
                .OrderByDescending(r => _socialExperience.FavoriteTeammates.ContainsKey(r.OwnerId)).ThenByDescending(r => r.MessageId).ToArray();
            string nextKey = string.Join('|', next.Select(r => $"{r.MessageId}:{r.Revision}:{r.Status}"));
            hint.Text = !_client.SupportsRecruitment ? "服务器尚未支持完整开黑招募，请更新 LOL-GameApi 到协议 v3 并重新连接。" :
                next.Length == 0 ? "暂无招募，登录 LOL 后可点击“开黑招募”发布。" :
                "报名后复制发起人游戏 ID，在 LOL 客户端添加好友组队。段位及位置由双方确认。";
            if (key == nextKey) return;
            long selected = list.SelectedIndex >= 0 ? states[list.SelectedIndex].MessageId : 0;
            states = next; key = nextKey;
            list.DataSource = states.Select(s => new LoungeListRow($"[{s.Status}] {s.Recruitment.Region} · {LoungeRules.Modes.GetValueOrDefault(s.Recruitment.QueueId)} · {s.OwnerName} · {s.Participants.Length}/{s.Recruitment.Needed}")).ToArray();
            list.SelectedIndex = Array.FindIndex(states, s => s.MessageId == selected);
            if (list.SelectedIndex < 0 && states.Length > 0) list.SelectedIndex = 0;
            open.Enabled = states.Length > 0;
        }
        open.Click += (_, _) =>
        {
            if (list.SelectedIndex < 0) return;
            using var detail = CreateRecruitmentDetails(states[list.SelectedIndex].MessageId);
            detail.ShowDialog(dialog);
            RefreshList();
        };
        list.CellDoubleClick += (_, _) => open.PerformClick();
        sameRegion.CheckedChanged += (_, _) => { key = "\0"; RefreshList(); }; active.CheckedChanged += (_, _) => { key = "\0"; RefreshList(); };
        style.SelectedIndexChanged += (_, _) => { key = "\0"; RefreshList(); }; position.SelectedIndexChanged += (_, _) => { key = "\0"; RefreshList(); };
        dialog.Controls.Add(list); dialog.Controls.Add(open); dialog.Controls.Add(hint); dialog.Controls.Add(filters);
        var timer = new System.Windows.Forms.Timer { Interval = 500 };
        timer.Tick += (_, _) => RefreshList(); timer.Start();
        dialog.Disposed += (_, _) => timer.Dispose();
        RefreshList(); AntdWindowChrome.AddHeader(dialog); UiTheme.Apply(dialog); return dialog;
    }

    internal Form CreateRecruitmentDetails(long id)
    {
        var dialog = RecruitmentDialog("开黑招募详情", new Size(620, 480));
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(16) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 156));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        var description = new AntdUI.Input { Name = "Description", Dock = DockStyle.Fill, ReadOnly = true,
            Multiline = true, BorderWidth = 0, TabStop = false };
        var participants = CreateLoungeList();
        participants.Name = "Participants";
        var feedback = new AntdUI.Label { Name = "Feedback", Dock = DockStyle.Fill, TextMultiLine = true, Text = "报名占用一个名额。复制游戏 ID 后，在 LOL 客户端添加好友组队；段位和位置请双方确认。" };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        var join = new AntdUI.Button { Name = "Join", Text = "报名加入", AutoSize = true };
        var leave = new AntdUI.Button { Name = "Leave", Text = "退出报名", AutoSize = true };
        var close = new AntdUI.Button { Name = "CloseRecruitment", Text = "结束招募", AutoSize = true };
        var copy = new AntdUI.Button { Text = "复制发起人 ID", AutoSize = true };
        var party = new AntdUI.Button { Name = "OpenPartyHub", Text = "进入开黑大厅", AutoSize = true };
        buttons.Controls.AddRange([join, leave, close, copy, party]);
        party.Click += (_, _) =>
        {
            var state = _client.Recruitments.FirstOrDefault(r => r.MessageId == id); if (state == null) return;
            if (_client.CurrentRegion != state.Recruitment.Region) { feedback.Text = "当前账号大区与招募不一致，请先切换大区"; return; }
            string suggested = state.OwnerName;
            if (state.OwnerId == _client.CurrentPlayerId && participants.SelectedIndex >= 0 && participants.SelectedIndex < state.Participants.Length)
                suggested = state.Participants[participants.SelectedIndex].Name;
            using var hub = new PartyHubForm(LOL_GameAssistant.Bootstrap.AppCompositionRoot.PartyService, suggested); hub.ShowDialog(dialog);
        };
        layout.Controls.Add(description, 0, 0); layout.Controls.Add(participants, 0, 1);
        layout.Controls.Add(feedback, 0, 2); layout.Controls.Add(buttons, 0, 3);
        dialog.Controls.Add(layout);
        bool busy = false;
        string key = "";
        void RefreshDetails()
        {
            var state = _client.Recruitments.FirstOrDefault(r => r.MessageId == id);
            if (state == null)
            {
                description.Text = "此招募尚未同步、已被删除，或来自旧版服务器。请重新连接后查看招募列表。";
                join.Enabled = leave.Enabled = close.Enabled = copy.Enabled = false;
                participants.DataSource = Array.Empty<LoungeListRow>(); return;
            }
            string availability = !_client.CanSend ? "连接频道并登录 LOL 后可报名或管理招募。" :
                _client.CurrentRegion != state.Recruitment.Region ? "当前账号大区与招募不同，切换到对应大区后可报名。" : "";
            string descriptionText = (RecruitmentSummary(state).Replace("\n双击查看详情、报名或管理招募。", "") +
                (availability.Length == 0 ? "" : "\n" + availability)).Replace("\n", "\r\n");
            if (description.Text != descriptionText) description.Text = descriptionText;
            string nextKey = $"{state.MessageId}:{state.Revision}";
            if (nextKey != key)
            {
                var selected = participants.SelectedIndex >= 0 && participants.DataSource is LoungeListRow[] previous && participants.SelectedIndex < previous.Length
                    ? previous[participants.SelectedIndex] : null;
                key = nextKey;
                var rows = state.Participants.Select(p => new LoungeListRow($"{p.Name} · {p.Region} · {p.Rank}")).ToArray();
                participants.DataSource = rows;
                participants.SelectedIndex = selected == null ? -1 : Array.IndexOf(rows, selected);
            }
            bool active = !state.Closed && state.ExpiresAt > DateTimeOffset.UtcNow;
            bool owner = state.OwnerId == _client.CurrentPlayerId;
            bool joined = state.Participants.Any(p => p.Id == _client.CurrentPlayerId);
            bool canChange = !busy && _client.CanSend && _client.SupportsRecruitment && active;
            join.Enabled = canChange && !owner && !joined && state.Participants.Length < state.Recruitment.Needed && _client.CurrentRegion == state.Recruitment.Region;
            leave.Enabled = canChange && joined;
            close.Enabled = canChange && owner;
            copy.Enabled = true;
        }
        async Task Change(string action)
        {
            if (busy) return;
            busy = true; RefreshDetails(); feedback.Text = "正在确认操作结果…";
            try
            {
                await _client.ChangeRecruitmentAsync(id, action);
                if (!dialog.IsDisposed) feedback.Text = action switch
                {
                    "join" => "报名成功！请复制发起人游戏 ID，在 LOL 客户端添加好友组队。",
                    "leave" => "已退出报名，名额已释放。", _ => "招募已结束。"
                };
            }
            catch (Exception ex) { if (!dialog.IsDisposed) feedback.Text = LoungeClient.SendError(ex); }
            finally { busy = false; if (!dialog.IsDisposed) RefreshDetails(); }
        }
        join.Click += async (_, _) => await Change("join");
        leave.Click += async (_, _) => await Change("leave");
        close.Click += async (_, _) => await Change("close");
        copy.Click += (_, _) =>
        {
            if (_client.Recruitments.FirstOrDefault(r => r.MessageId == id) is not { } state) return;
            try { Clipboard.SetText(state.OwnerName); feedback.Text = "已复制发起人 ID，请在 LOL 客户端添加好友。"; }
            catch (System.Runtime.InteropServices.ExternalException) { feedback.Text = "剪贴板忙，请重试。"; }
        };
        var timer = new System.Windows.Forms.Timer { Interval = 500 };
        timer.Tick += (_, _) => RefreshDetails(); timer.Start();
        dialog.Disposed += (_, _) => timer.Dispose();
        RefreshDetails(); AntdWindowChrome.AddHeader(dialog); UiTheme.Apply(dialog);
        return dialog;
    }
}
