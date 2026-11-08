using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public partial class HomeForm
{
    private async Task LoadCompleteHistoryAsync(PlayerProfile player, MatchHistoryResponse seed,
        Func<bool> isCurrent, CancellationToken token)
    {
        string session = PlayerHistorySession.Key;
        _modeScope.Text = "正在读取全部历史…";
        try
        {
            var full = await CompletePlayerHistoryReader.ReadAsync(_matchHistoryService, player.Puuid,
                _historySnapshot?.IsStale == true ? null : seed, page =>
                {
                    if (!isCurrent() || session != PlayerHistorySession.Key) return;
                    UpdateModeCounts(page);
                }, token);
            if (!isCurrent() || session != PlayerHistorySession.Key) return;
            matchlists = full;
            UpdateModeOptions();
            _updatingPagination = true;
            try
            {
                int page = game_pagin.Current;
                game_pagin.Total = full.Games!.Games.Count(MatchesSelectedMode);
                game_pagin.Current = Math.Clamp(page, 1, Math.Max(1, (game_pagin.Total + game_pagin.PageSize - 1) / game_pagin.PageSize));
            }
            finally { _updatingPagination = false; }
            lblHistoryTitle.Text = "历史战绩";
            string scope = _modeScope.Text + "；统计范围为接口当前可返回的全部历史，不限日期或赛季。";
            refeash.AccessibleDescription = scope;
            foreach (Control control in new Control[] { _modeScope, lblHistoryTitle, game_pagin, refeash }) _historyTip.SetToolTip(control, scope);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!isCurrent()) return;
            _modeScope.Text = "全部历史读取未完成 · 点击刷新重试";
            _historyTip.SetToolTip(_modeScope, error.Message + "；当前场次仅为已读取部分，不能当作总场次。");
            RuntimeDiagnostics.Report("首页全部历史", "未完成", error.Message);
        }
    }
}
