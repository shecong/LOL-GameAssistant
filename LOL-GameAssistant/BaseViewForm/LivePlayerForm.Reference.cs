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
    internal bool RecentEncounterUnavailable { get; private set; }
    private readonly AntdUI.Label _kdaLabel = new() { Name = "lblRecentKda", Text = "KDA —",
        AutoSize = false, AutoEllipsis = true, BackColor = Color.Transparent, Font = new Font(UiMetrics.FontFamily, 8F) };
    private readonly AntdUI.Label _encounterLabel = new() { Name = "lblRecentEncounter", Text = "遇见检测中",
        AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleRight,
        BackColor = Color.Transparent, Font = new Font(UiMetrics.FontFamily, 8F) };

    internal void SetRecentEncounterHistory(MatchHistoryResponse history)
    {
        RecentEncounterHistory = SelectEncounterHistory(history);
        RecentEncounterUnavailable = false;
        OverviewChanged?.Invoke(this, EventArgs.Empty);
    }

    internal static IReadOnlyList<MatchHistoryGame> SelectEncounterHistory(MatchHistoryResponse history) =>
        history.Games?.Games.Where(game => game.GameId > 0 && game.IsCompletedGame() && !game.IsCustomGame())
            .DistinctBy(game => game.GameId).OrderByDescending(game => game.GameCreation).Take(50).ToArray() ?? [];

    internal void UpdateRecentEncounter(string? myPuuid, IReadOnlyList<MatchHistoryGame>? ownHistory, bool ownUnavailable = false)
    {
        _encounterLabel.Visible = !_isBot && _playerPuuid != myPuuid;
        if (!_encounterLabel.Visible) return;
        var ownIds = ownHistory?.Select(game => game.GameId).ToHashSet();
        var shared = ownIds == null ? [] : RecentEncounterHistory?.Where(game => ownIds.Contains(game.GameId)).ToArray() ?? [];
        bool historyLoading = RecentEncounterHistory == null && !RecentEncounterUnavailable;
        _encounterLabel.Text = string.IsNullOrWhiteSpace(myPuuid) ? "遇见检测待确认身份"
            : historyLoading ? "战绩加载后检测"
            : ownHistory == null ? ownUnavailable ? "本人战绩暂不可用" : "等待读取本人战绩"
            : RecentEncounterHistory == null ? RecentEncounterUnavailable ? "玩家战绩暂不可用" : "等待读取玩家战绩"
            : shared.Length > 0 ? $"近期遇到 {shared.Length} 次" : "近期未遇到";
        _encounterLabel.ForeColor = shared.Length > 0 ? UiTheme.Palette.Accent : UiTheme.Palette.TextSecondary;
        _performanceTip.SetToolTip(_encounterLabel,
            "比对你和该玩家各自最近50场非自定义有效对局，不限日期；仅统计相同对局 ID。" +
            (!historyLoading && (ownHistory == null ? ownUnavailable : RecentEncounterUnavailable) ? "战绩暂不可用时，可点击上方“获取数据”重试。" :
            shared.Length > 0 ? $"最近一次：{DateTimeOffset.FromUnixTimeMilliseconds(shared.Max(game => game.GameCreation)).ToLocalTime():MM-dd HH:mm}" : "未遇到仅代表此统计范围内无共同记录。"));
    }

    internal void ShowRecentAssessment(RecentModePerformanceAssessment assessment)
    {
        _kdaLabel.Text = assessment.SampleSize > 0
            ? $"KDA {assessment.Kda:F2} · {assessment.SampleSize}场 · {(assessment.UsesAllModes ? "各模式" : "同模式")}" : $"KDA — · {assessment.NoKdaSampleReason ?? "无有效KDA样本"}";
        _kdaLabel.ForeColor = UiTheme.Palette.TextPrimary;
        _performanceTip.SetToolTip(_kdaLabel, $"{_historySnapshot?.ScopeText}\n{assessment.Detail}");
    }

    private bool UseLoadedAssessmentOnFailure(string reason)
    {
        if (_cachedAssessment is not { SampleSize: > 0 } assessment) return false;
        lblSummary.Text = RecentPerformanceLabelFormatter.GetText(assessment);
        ShowRecentAssessment(assessment);
        string detail = $"{reason}。当前评分保留已获取摘要的计算结果。{assessment.Detail}";
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
