using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public partial class HomeForm
{
    private bool _cachedModeCountsVisible;

    private async Task<CompletePlayerHistorySnapshot?> ShowCachedModeCountsAsync(PlayerProfile player,
        Func<bool> isCurrent, CancellationToken token)
    {
        if (_matchHistoryService is not ICompletePlayerHistoryStore store) return null;
        try
        {
            var cached = await store.ReadCompleteHistoryAsync(player.Puuid, token);
            if (!isCurrent() || cached == null) return null;
            UpdateModeCounts(cached.Data);
            _cachedModeCountsVisible = true;
            _modeScope.Text = $"缓存统计：{cached.Data.Games!.Games.Count} 场 · 更新中";
            _historyTip.SetToolTip(_modeScope, $"上次更新：{cached.UpdatedAt.ToLocalTime():MM-dd HH:mm}；正在检查新增对局，完整历史每天校准一次。");
            return cached;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { RuntimeDiagnostics.Report("首页统计缓存", "暂不可用", error.GetType().Name); return null; }
    }

    private async Task LoadCompleteHistoryAsync(PlayerProfile player, MatchHistoryResponse seed,
        Func<bool> isCurrent, CancellationToken token, CompletePlayerHistorySnapshot? cached = null)
    {
        string session = PlayerHistorySession.Key;
        if (cached == null) _modeScope.Text = "正在读取全部历史…";
        try
        {
            var refreshed = await CompletePlayerHistoryRefresh.ReadAsync(_matchHistoryService, player.Puuid,
                _historySnapshot?.IsStale == true ? null : seed, cached, page =>
                {
                    if (!isCurrent() || session != PlayerHistorySession.Key) return;
                    void Apply()
                    {
                        if (!isCurrent() || session != PlayerHistorySession.Key) return;
                        // 全量校准期间保留旧统计，避免场次从零开始跳动。
                        if (cached != null && page.Games?.HasMore == true) return;
                        UpdateModeCounts(page);
                    }
                    if (InvokeRequired) BeginInvoke((Action)Apply); else Apply();
                }, token);
            if (!isCurrent() || session != PlayerHistorySession.Key) return;
            var full = refreshed.Data;
            _cachedModeCountsVisible = false;
            UpdateModeCounts(full);
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
            _modeScope.Text = cached == null ? "全部历史读取未完成 · 点击刷新重试" : $"缓存统计：{cached.Data.Games!.Games.Count} 场 · 更新失败，可刷新重试";
            _historyTip.SetToolTip(_modeScope, error.Message + "；当前场次仅为已读取部分，不能当作总场次。");
            RuntimeDiagnostics.Report("首页全部历史", "未完成", error.Message);
        }
    }
}
