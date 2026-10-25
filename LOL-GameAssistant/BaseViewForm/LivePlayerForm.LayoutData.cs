using LOL_GameAssistant.Domain.Matches;

namespace LOL_GameAssistant.BaseViewForm;

public partial class LivePlayerForm
{
    // Alternate layouts read the existing card; they never own its images or start a history request.
    internal string LayoutName => lblName.Text ?? "读取玩家中";
    internal string LayoutPosition => string.IsNullOrWhiteSpace(_position) ? "位置待确认" : GetPositionText(_position);
    internal string LayoutRank => string.IsNullOrWhiteSpace(_rankLabel.Text) ? "段位待获取" : _rankLabel.Text;
    internal string LayoutAssessment => lblSummary.Text ?? "";
    internal string LayoutKda => _cachedAssessment is { SampleSize: > 0 } value ? value.Kda.ToString("F2") : "—";
    internal double? LayoutKdaValue => _cachedAssessment is { SampleSize: > 0 } value ? value.Kda : null;
    internal string LayoutWinRate => RecentSampleSize > 0 ? $"{100.0 * RecentWins / RecentSampleSize:0}%" : "—";
    internal Image? LayoutPortrait => picProfile.Image;
    internal Image? LayoutRankEmblem
    {
        get
        {
            string rank = LayoutRank.ToLowerInvariant();
            (string Chinese, string Key, string Resource)[] tiers = [("王者", "challenger", "09"), ("宗师", "grandmaster", "08"),
                ("大师", "master", "07"), ("钻石", "diamond", "05"), ("翡翠", "emerald", "06"), ("铂金", "platinum", "platinum"),
                ("黄金", "gold", "04"), ("白银", "silver", "03"), ("青铜", "bronze", "02"), ("黑铁", "iron", "01")];
            foreach (var tier in tiers)
                if (rank.Contains(tier.Chinese) || rank.Contains(tier.Key))
                    return Properties.Resources.ResourceManager.GetObject(tier.Resource) as Image;
            return Properties.Resources.ResourceManager.GetObject("unranked") as Image;
        }
    }
    private string _layoutPremadeNote = "";
    internal string LayoutNote => string.Join(" · ", new[] { _layoutPremadeNote, _encounterLabel.Text }
        .Where(text => !string.IsNullOrWhiteSpace(text)));
    internal LiveHistoryFilter HistoryFilter { get; private set; }
    internal void SetHistoryFilter(LiveHistoryFilter filter)
    {
        if (HistoryFilter == filter) return;
        HistoryFilter = filter;
        _layoutHistorySource = null;
        if (_historySnapshot?.Data is { } history) RenderVisibleHistory(history);
    }
    private Application.Matches.PlayerHistorySnapshot? _layoutHistorySource;
    private IReadOnlyList<MatchHistoryGame> _layoutHistory = [];
    internal IReadOnlyList<MatchHistoryGame> LayoutHistory
    {
        get
        {
            if (_layoutHistorySource != _historySnapshot)
            {
                _layoutHistorySource = _historySnapshot;
                _layoutHistory = _historySnapshot?.Data?.Games is { } page
                    ? LiveHistorySelection.Select(page.Games, HistoryFilter, _currentQueueId, _currentGameMode) : [];
            }
            return _layoutHistory;
        }
    }
    internal string LayoutHistoryStatus => _isBot ? "机器人无玩家战绩" : _historySnapshot?.DataWarning is { Length: > 0 } warning
        ? warning : _historySnapshot?.Available == true ? (HistoryFilter == LiveHistoryFilter.CurrentMode ? "已获取摘要中暂无当前模式战绩" : "暂无战绩") : RecentEncounterUnavailable ? "战绩暂不可用，请点击获取数据重试" : "正在加载战绩…";

    internal RecentMatchRow CreateLayoutHistoryRow(MatchHistoryGame summary)
    {
        var row = new RecentMatchRow(_gameAssetService, _matchHistoryService);
        row.UseWorkspaceLayout();
        _ = row.SetSummaryAsync(summary, _playerPuuid ?? "");
        row.EnableAutomaticHonors();
        return row;
    }
}
