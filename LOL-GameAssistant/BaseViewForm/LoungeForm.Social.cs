using LOL.Lounge;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public sealed partial class LoungeForm
{
    private readonly CancellationTokenSource _socialLifetime = new();
    private PlayerExperience _socialExperience = new();
    private string _socialAccount = "";
    private async Task LoadSocialPreferencesAsync()
    {
        string account = _client.CurrentPlayerId;
        if (_socialAccount == account) return;
        _socialAccount = account; _socialExperience = new(); _memberKey = "";
        if (string.IsNullOrWhiteSpace(account)) return;
        try
        {
            var value = await AppCompositionRoot.CompanionExperienceStore.LoadAsync("lounge:" + account, _socialLifetime.Token);
            if (!IsDisposed && account == _client.CurrentPlayerId) { _socialExperience = value; _memberKey = ""; }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { RuntimeDiagnostics.WriteException(e); }
    }
    private async Task ToggleFavoriteAsync(LoungeMember member)
    {
        string account = _client.CurrentPlayerId;
        if (account.Length == 0) { _feedback.Text = "登录 LOL 后可保存常用队友。"; return; }
        try
        {
            await AppCompositionRoot.CompanionExperienceStore.UpdateAsync("lounge:" + account, value =>
            {
                if (!value.FavoriteTeammates.Remove(member.Id)) value.FavoriteTeammates[member.Id] = member.Name;
            }, _socialLifetime.Token);
            var value = await AppCompositionRoot.CompanionExperienceStore.LoadAsync("lounge:" + account, _socialLifetime.Token);
            if (!IsDisposed && account == _client.CurrentPlayerId) { _socialExperience = value; _memberKey = ""; _feedback.Text = "常用队友已更新；可在更多菜单再次组队。"; RefreshView(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!IsDisposed) _feedback.Text = "保存队友失败：" + e.Message; }
    }
    private void ShowFavoriteTeammates()
    {
        using var dialog = RecruitmentDialog("常用队友 · 再次组队", new Size(600, 420));
        var list = CreateLoungeList();
        var favorites = _socialExperience.FavoriteTeammates.ToArray();
        list.DataSource = favorites.Select(pair => new LoungeListRow(pair.Value + (_client.Members.Any(m => m.Id == pair.Key) ? " · 助手在线" : " · 助手离线"))).ToArray();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48 };
        var party = new AntdUI.Button { Text = "打开大厅并预选好友", AutoSize = true };
        party.Click += (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= favorites.Length) return;
            using var hub = new PartyHubForm(AppCompositionRoot.PartyService, favorites[list.SelectedIndex].Value); hub.ShowDialog(dialog);
        };
        var copy = new AntdUI.Button { Text = "复制完整游戏 ID", AutoSize = true };
        copy.Click += (_, _) => { if (list.SelectedIndex >= 0 && list.SelectedIndex < favorites.Length) TryCopyText(favorites[list.SelectedIndex].Value); };
        buttons.Controls.AddRange([party, copy]); dialog.Controls.Add(list); dialog.Controls.Add(buttons);
        AntdWindowChrome.AddHeader(dialog); UiTheme.Apply(dialog); dialog.ShowDialog(FindForm());
    }
    private void ShowSocialPreferences()
    {
        string openedAccount = _client.CurrentPlayerId;
        using var dialog = RecruitmentDialog("组队偏好", new Size(560, 330));
        var rows = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), WrapContents = true, FlowDirection = FlowDirection.TopDown };
        var style = new AntdUI.Select { Name = "playStyle", List = true, DropDownArrow = true, Width = 280, Height = 38 };
        style.Items.AddRange(RecruitmentPreferences.Styles.Cast<object>().ToArray()); style.SelectedValue = _socialExperience.PlayStyle;
        if (style.SelectedIndex < 0) style.SelectedIndex = 0;
        var voice = new AntdUI.Checkbox { Text = "愿意语音", AutoSize = true, Checked = _socialExperience.Voice };
        var time = new AntdUI.Input { Width = 440, Height = 38, MaxLength = 30, PlaceholderText = "常玩时间，例如 工作日20:00–22:00", Text = _socialExperience.AvailableTime };
        var status = new AntdUI.Label { Width = 480, Height = 55, TextMultiLine = true, Text = "偏好将写入招募说明，与补充说明合计最多120字；不会自动发送邀请。" };
        var save = new AntdUI.Button { Text = "保存偏好", AutoSize = true };
        save.Click += async (_, _) =>
        {
            string account = _client.CurrentPlayerId;
            if (account.Length == 0) { status.Text = "登录 LOL 后可按账号保存组队偏好。"; return; }
            if (account != openedAccount) { status.Text = "账号已切换，请关闭后重新打开组队偏好。"; return; }
            if (save.Loading) return; save.Loading = true;
            try
            {
                await AppCompositionRoot.CompanionExperienceStore.UpdateAsync("lounge:" + account, value =>
                { value.PlayStyle = style.SelectedValue?.ToString() ?? RecruitmentPreferences.Styles[0]; value.Voice = voice.Checked; value.AvailableTime = time.Text ?? ""; }, _socialLifetime.Token);
                var value = await AppCompositionRoot.CompanionExperienceStore.LoadAsync("lounge:" + account, _socialLifetime.Token);
                if (account == _client.CurrentPlayerId && !IsDisposed) _socialExperience = value;
                if (!dialog.IsDisposed) dialog.Close();
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!dialog.IsDisposed) status.Text = e.Message; }
            finally { if (!save.IsDisposed) save.Loading = false; }
        };
        rows.Controls.AddRange([style, voice, time, status, save]); dialog.Controls.Add(rows); AntdWindowChrome.AddHeader(dialog); UiTheme.Apply(dialog); dialog.ShowDialog(FindForm());
    }
}
