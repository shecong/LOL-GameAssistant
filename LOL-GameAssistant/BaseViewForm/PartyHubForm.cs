using Button = AntdUI.Button;
using Label = AntdUI.Label;
using LOL_GameAssistant.Application.Lobby;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>从招募到当前客户端大厅的手动组队入口，不按聊天昵称推断 PUUID。</summary>
internal sealed class PartyHubForm : AntdUI.Window, IThemeAware
{
    private readonly IPartyService _service;
    private readonly UiRequestScope _requests = new();
    private readonly AntdUI.Select _queues = new() { Width = 260, Height = 38, ReadOnly = true };
    private readonly AntdUI.Select _friends = new() { Width = 280, Height = 38, ReadOnly = true };
    private readonly AntdUI.Select _invitations = new() { Width = 280, Height = 38, ReadOnly = true };
    private readonly AntdUI.Select _first = new() { Width = 125, Height = 38, ReadOnly = true };
    private readonly AntdUI.Select _second = new() { Width = 125, Height = 38, ReadOnly = true };
    private readonly AntdUI.Label _state = new() { Dock = DockStyle.Bottom, Height = 70, Padding = new Padding(14) };
    private readonly AntdUI.Label _members = new() { Dock = DockStyle.Fill, Padding = new Padding(16), Text = "刷新后显示当前队伍" };
    private readonly string? _suggestedName;
    private bool _busy;
    private sealed record Choice<T>(T Value, string Label) { public override string ToString() => Label; }
    internal PartyHubForm(IPartyService service, string? suggestedName = null)
    {
        _service = service; _suggestedName = suggestedName; Text = "开黑大厅"; Size = new Size(820, 620); MinimumSize = new Size(640, 530); StartPosition = FormStartPosition.CenterParent;
        var body = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 275, Padding = new Padding(14), WrapContents = true, FlowDirection = FlowDirection.LeftToRight };
        var title = new AntdUI.Label { Text = "选择模式 → 创建大厅 → 邀请好友 → 设置位置 → 开始匹配", Width = 720, Height = 40 };
        body.SizeChanged += (_, _) => title.Width = Math.Max(300, body.ClientSize.Width - 34);
        body.Controls.Add(title); body.SetFlowBreak(title, true);
        void Button(string label, Func<CancellationToken, Task<PartyActionResult>> action) { var b = new AntdUI.Button { Text = label, AutoSize = true }; b.Click += async (_, _) => await Run(action); body.Controls.Add(b); }
        body.Controls.Add(_queues); Button("创建所选大厅", t => _queues.SelectedValue is Choice<int> q ? _service.CreateAsync(q.Value, t) : Task.FromResult(new PartyActionResult(false, "请先刷新并选择当前可用模式")));
        var refresh = new AntdUI.Button { Text = "刷新大厅", AutoSize = true }; refresh.Click += async (_, _) => await RefreshAsync(); body.Controls.Add(refresh); body.SetFlowBreak(refresh, true);
        body.Controls.Add(_friends); Button("邀请所选好友", t => _friends.SelectedValue is Choice<string> f ? _service.InviteFriendAsync(f.Value, t) : Task.FromResult(new PartyActionResult(false, "请选择已经建立好友关系的在线玩家"))); body.SetFlowBreak(body.Controls[^1], true);
        body.Controls.Add(_invitations); Button("接受所选邀请", t => _invitations.SelectedValue is Choice<string> i ? _service.AcceptInvitationAsync(i.Value, t) : Task.FromResult(new PartyActionResult(false, "没有选中有效邀请"))); body.SetFlowBreak(body.Controls[^1], true);
        var positions = new[] { ("TOP", "上路"), ("JUNGLE", "打野"), ("MIDDLE", "中路"), ("BOTTOM", "下路"), ("UTILITY", "辅助"), ("FILL", "补位") };
        foreach (var (key, label) in positions) { _first.Items.Add(new Choice<string>(key, label)); _second.Items.Add(new Choice<string>(key, label)); }
        _first.SelectedIndex = 0; _second.SelectedIndex = 1; body.Controls.Add(_first); body.Controls.Add(_second);
        Button("设置首选 / 次选", t => _service.SetPositionsAsync((_first.SelectedValue as Choice<string>)?.Value ?? "", (_second.SelectedValue as Choice<string>)?.Value ?? "", t));
        Button("开始匹配", t => _service.StartSearchAsync(t));
        Controls.Add(_members); Controls.Add(body); Controls.Add(_state); AntdWindowChrome.AddHeader(this);
        Shown += async (_, _) => await RefreshAsync(); Disposed += (_, _) => _requests.Dispose(); UiTheme.Apply(this);
    }
    private async Task RefreshAsync()
    {
        if (_busy || IsDisposed) return;
        using var r = _requests.Begin(); _state.Text = "正在读取当前客户端的队列与好友…";
        try
        {
            var queuesTask = _service.GetQueuesAsync(r.Token); var stateTask = _service.GetSnapshotAsync(r.Token); await Task.WhenAll(queuesTask, stateTask);
            if (!r.IsCurrent || IsDisposed) return;
            var state = stateTask.Result; int? selected = (_queues.SelectedValue as Choice<int>)?.Value;
            _queues.Items.Clear(); foreach (var q in queuesTask.Result.Where(q => q.Enabled)) _queues.Items.Add(new Choice<int>(q.Id, $"{q.Name} · {q.Id}"));
            _queues.SelectedValue = _queues.Items.OfType<Choice<int>>().FirstOrDefault(q => q.Value == (selected ?? state.QueueId));
            if (_queues.SelectedIndex < 0 && _queues.Items.Count > 0) _queues.SelectedIndex = 0;
            _friends.Items.Clear(); foreach (var f in state.Friends.Where(f => f.Online)) _friends.Items.Add(new Choice<string>(f.Puuid, f.Name));
            var suggested = _friends.Items.OfType<Choice<string>>().Where(f => string.Equals(f.Label, _suggestedName, StringComparison.OrdinalIgnoreCase)).ToArray();
            _friends.SelectedValue = suggested.Length == 1 ? suggested[0] : null;
            _invitations.Items.Clear(); foreach (var i in state.Invitations) _invitations.Items.Add(new Choice<string>(i.Id, i.Name)); if (_invitations.Items.Count > 0) _invitations.SelectedIndex = 0;
            _members.Text = $"当前队列：{state.QueueId}\n\n" + string.Join("\n", state.Members.Select(m => $"{(m.Leader ? "★ 队长" : "队员")} · {m.Name} · {m.Position}"));
            _state.Text = state.Message + (_suggestedName == null ? "" : $"\n招募对象：{_suggestedName}；仅在当前好友列表中精确匹配，同名不替代身份验证。");
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (r.IsCurrent && !IsDisposed) _state.Text = "读取失败：" + e.Message; }
    }
    private async Task Run(Func<CancellationToken, Task<PartyActionResult>> action)
    {
        if (_busy || IsDisposed) return; _busy = true;
        try { var result = await action(_requests.Token); if (IsDisposed) return; _busy = false; await RefreshAsync(); if (!IsDisposed) _state.Text = result.Message + "\n" + _state.Text; }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!IsDisposed) _state.Text = e.Message; }
        finally { _busy = false; }
    }
    public void ApplyTheme(ThemePalette p) { BackColor = p.Surface; _members.ForeColor = p.TextPrimary; _state.ForeColor = p.TextSecondary; }
}
