using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.MatchAnalysis;

namespace LOL_GameAssistant.BaseViewForm;

public partial class LivePlayerForm
{
    internal int? PlayerLevel { get; private set; }
    internal int RecentWins { get; private set; }
    internal int RecentSampleSize { get; private set; }
    internal event EventHandler? OverviewChanged;
    private AntdUI.Label? _historyHeading;
    internal IReadOnlyList<MatchHistoryGame>? RecentEncounterHistory { get; private set; }
    private RecentModePerformanceAssessment? _loadedAssessment;
    private readonly AntdUI.Label _kdaLabel = new() { Name = "lblRecentKda", Text = "KDA —",
        AutoSize = false, AutoEllipsis = true, BackColor = Color.Transparent, Font = new Font(UiMetrics.FontFamily, 8F) };
    private readonly AntdUI.Label _encounterLabel = new() { Name = "lblRecentEncounter", Text = "遇见检测中",
        AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleRight,
        BackColor = Color.Transparent, Font = new Font(UiMetrics.FontFamily, 8F) };

    internal void SetRecentEncounterHistory(MatchHistoryResponse history)
    {
        long cutoff = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeMilliseconds();
        RecentEncounterHistory = history.Games?.Games.Where(game => game.GameId > 0 &&
            game.GameCreation >= cutoff && game.IsCompletedGame()).DistinctBy(game => game.GameId)
            .OrderByDescending(game => game.GameCreation).Take(HistoryFetchCount).ToArray() ?? [];
        OverviewChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void UpdateRecentEncounter(string? myPuuid, IReadOnlyList<MatchHistoryGame>? ownHistory)
    {
        _encounterLabel.Visible = !_isBot && _playerPuuid != myPuuid;
        if (!_encounterLabel.Visible) return;
        var ownIds = ownHistory?.Select(game => game.GameId).ToHashSet();
        var shared = ownIds == null ? [] : RecentEncounterHistory?.Where(game => ownIds.Contains(game.GameId)).ToArray() ?? [];
        _encounterLabel.Text = string.IsNullOrWhiteSpace(myPuuid) ? "遇见检测待确认身份"
            : ownHistory == null || RecentEncounterHistory == null ? "遇见检测中"
            : shared.Length > 0 ? $"近期遇到 {shared.Length} 次" : "近期未遇到";
        _encounterLabel.ForeColor = shared.Length > 0 ? UiTheme.Palette.Accent : UiTheme.Palette.TextSecondary;
        _performanceTip.SetToolTip(_encounterLabel,
            "比对你和该玩家最近已加载的最多 100 场、30 天内有效对局；仅统计相同对局 ID。" +
            (shared.Length > 0 ? $"最近一次：{DateTimeOffset.FromUnixTimeMilliseconds(shared.Max(game => game.GameCreation)).ToLocalTime():MM-dd HH:mm}" : "未遇到仅代表此统计范围内无共同记录。"));
    }

    internal void ShowRecentAssessment(RecentModePerformanceAssessment assessment)
    {
        _kdaLabel.Text = assessment.SampleSize > 0
            ? $"KDA {assessment.Kda:F2} · {assessment.SampleSize}场 · {(assessment.UsesAllModes ? "各模式" : "同模式")}" : "KDA — · 无有效对局";
        _kdaLabel.ForeColor = UiTheme.Palette.TextPrimary;
        _performanceTip.SetToolTip(_kdaLabel, assessment.Detail);
    }

    private void ShowLoadedRecentKda(MatchHistoryResponse history,
        IReadOnlyList<(MatchDetail detail, MatchParticipant gamer)> results)
    {
        long cutoff = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeMilliseconds();
        var eligible = history.Games!.Games.Where(game => game.GameCreation >= cutoff && game.IsCompletedGame())
            .Select(game => game.GameId).ToHashSet();
        var samples = results.Where(item => eligible.Contains(item.detail.GameId) && item.gamer.stats?.HasKdaResult == true)
            .DistinctBy(item => item.detail.GameId).ToArray();
        var sameMode = samples.Where(item => history.Games.Games.Any(game => game.GameId == item.detail.GameId &&
            MatchModeComparer.IsSameMode(_currentQueueId, _currentGameMode, game))).ToArray();
        bool allModes = sameMode.Length == 0;
        if (!allModes) samples = sameMode;
        if (samples.Length == 0) return;
        var assessment = RecentModePerformanceEvaluator.Evaluate("已加载近期对局",
            samples.Select(item => new MatchPerformanceAssessment(MatchPerformanceTier.Medium, 0, "",
                item.gamer.stats!.kills, item.gamer.stats.deaths, item.gamer.stats.assists)),
            samples.Select(item => item.gamer.IsWin())) with { UsesAllModes = allModes };
        _loadedAssessment = assessment;
        ShowRecentAssessment(assessment);
    }

    private bool UseLoadedAssessmentOnFailure(string reason)
    {
        if (_loadedAssessment is not { SampleSize: > 0 } assessment) return false;
        lblSummary.Text = RecentPerformanceLabelFormatter.GetText(assessment);
        ShowRecentAssessment(assessment);
        string detail = $"{reason}。当前评分仅统计已加载的有效对局。{assessment.Detail}";
        _performanceTip.SetToolTip(lblSummary, detail);
        _performanceTip.SetToolTip(_kdaLabel, detail);
        PublishRecentPerformance(assessment);
        return true;
    }

    private void LayoutReferenceHeader()
    {
        if (_historyHeading == null)
        {
            _historyHeading = new AntdUI.Label { Text = "最近战绩", AutoSize = false,
                BackColor = Color.Transparent, Font = new Font(UiMetrics.FontFamily, 9F, FontStyle.Bold) };
            headerPanel.Controls.Add(_historyHeading);
            headerPanel.Controls.Add(_kdaLabel);
            headerPanel.Controls.Add(_encounterLabel);
        }
        headerPanel.Height = 154;
        int right = Math.Max(220, ClientSize.Width - 12);
        picProfile.SetBounds(12, 12, 52, 52);
        lblName.Font = new Font(UiMetrics.FontFamily, 11F, FontStyle.Bold);
        int rankWidth = !string.IsNullOrWhiteSpace(_rankLabel.Text) ? Math.Max(60, TextRenderer.MeasureText(_rankLabel.Text, _rankLabel.Font).Width + 10) : 0;
        lblName.SetBounds(74, 10, Math.Max(60, right - 82 - rankWidth), 26);
        lblSummary.Visible = true;
        lblSummary.AutoEllipsis = true;
        lblSummary.Font = new Font(UiMetrics.FontFamily, 8F);
        lblSummary.SetBounds(right - 76, 40, 76, 22);
        _rankLabel.SetBounds(right - rankWidth, 12, rankWidth, 22);
        lblTeamTag.SetBounds(right - 40, 80, 40, 20);
        lblSub.SetBounds(74, 40, Math.Max(60, right - 158), 22);
        picCurrent.SetBounds(12, 70, 36, 36);
        lblChampionNow.Visible = !string.IsNullOrEmpty(lblChampionNow.Text);
        lblChampionNow.SetBounds(54, 78, Math.Max(110, right - 194), 20);
        lblPremadeTag.SetBounds(right - 132, 80, 86, 20);
        int encounterWidth = Math.Min(120, (right - 24) / 2);
        _kdaLabel.SetBounds(12, 106, Math.Max(80, right - encounterWidth - 20), 22);
        _encounterLabel.SetBounds(right - encounterWidth, 106, encounterWidth, 22);
        _historyHeading.SetBounds(12, 130, right - 12, 24);
        _historyHeading.ForeColor = UiTheme.Palette.TextPrimary;
        panelMatches.Padding = new Padding(8, 4, 8, 8);
    }
}
