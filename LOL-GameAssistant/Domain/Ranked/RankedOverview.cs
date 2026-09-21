namespace LOL_GameAssistant.Domain.Ranked;

/// <summary>
/// 召唤师可展示的排位概览。
/// LCU 返回字段的差异由基础设施层吸收，界面只读取稳定的领域读模型。
/// </summary>
public sealed class RankedOverview
{
    public Dictionary<string, RankedQueue> Queues { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, DateTime> SeasonEndsAt { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>按队列代码获取排位信息，不存在时返回空。</summary>
    public RankedQueue? GetQueue(string queueType) =>
        Queues.TryGetValue(queueType, out RankedQueue? queue) ? queue : null;

    /// <summary>
    /// 获取指定队列赛季结束日期；优先目标队列，再依次回退到单双排、灵活组排和任意可用数据。
    /// </summary>
    public string GetSeasonEndText(string queueType)
    {
        string[] preferredQueues =
        {
            queueType,
            RankedQueues.Solo5x5,
            RankedQueues.Flex5x5
        };

        foreach (string key in preferredQueues)
        {
            if (SeasonEndsAt.TryGetValue(key, out DateTime end))
                return end.ToString("yyyy-MM-dd");
        }

        DateTime fallback = SeasonEndsAt.Values.FirstOrDefault();
        return fallback == default ? "-" : fallback.ToString("yyyy-MM-dd");
    }
}

/// <summary>单个排位队列的稳定展示数据。</summary>
public sealed class RankedQueue
{
    public string QueueType { get; init; } = "";
    public string Tier { get; init; } = "";
    public string Division { get; init; } = "";
    public int LeaguePoints { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
    public bool IsProvisional { get; init; }
    public int ProvisionalGamesRemaining { get; init; }
    public int ProvisionalGameThreshold { get; init; }
    public string MiniSeriesProgress { get; init; } = "";
    public int RatedRating { get; init; }
    public string RatedTier { get; init; } = "";
    public string HighestTier { get; init; } = "";
    public string HighestDivision { get; init; } = "";

    public int TotalGames => Wins + Losses;

    public double WinRate => TotalGames > 0 ? Math.Round((double)Wins / TotalGames * 100, 1) : 0;

    /// <summary>将 LCU 的定位赛字段转换为用户可读文本。</summary>
    public string GetPlacementText()
    {
        if (!IsProvisional) return "已完成";

        int threshold = ProvisionalGameThreshold > 0 ? ProvisionalGameThreshold : 10;
        int played = Math.Max(0, threshold - ProvisionalGamesRemaining);
        return ProvisionalGamesRemaining > 0
            ? $"第 {played + 1}/{threshold} 场"
            : "已完成";
    }

    /// <summary>将晋级赛进度转换为用户可读文本。</summary>
    public string GetPromotionText()
    {
        if (string.IsNullOrWhiteSpace(MiniSeriesProgress)) return "非晋级赛";

        string progress = MiniSeriesProgress.ToUpperInvariant();
        int wins = progress.Count(item => item == 'W');
        int losses = progress.Count(item => item == 'L');
        int winsNeeded = Math.Max(1, (progress.Length + 1) / 2);
        return $"进行中 {wins}胜{losses}负（还需{Math.Max(0, winsNeeded - wins)}胜）";
    }
}

/// <summary>国服客户端排位队列标识。</summary>
public static class RankedQueues
{
    public const string Solo5x5 = "RANKED_SOLO_5x5";
    public const string Flex5x5 = "RANKED_FLEX_SR";
}

/// <summary>排位展示规则。</summary>
public static class RankedDisplayRules
{
    public static bool HasRank(RankedQueue? queue) =>
        queue != null &&
        !string.IsNullOrWhiteSpace(queue.Tier) &&
        !string.Equals(queue.Tier, "NONE", StringComparison.OrdinalIgnoreCase);

    public static string GetRatedTierName(string? ratedTier) => ratedTier?.ToUpperInvariant() switch
    {
        "GRAY" => "灰",
        "GREEN" => "绿",
        "BLUE" => "蓝",
        "PURPLE" => "紫",
        "ORANGE" => "橙",
        _ => ""
    };
}
