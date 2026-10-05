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

    /// <summary>计算统计中的总对局数。</summary>
    public int TotalGames => Wins + Losses;

    /// <summary>根据统计中的胜负数据或比例生成胜率值。</summary>
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
    /// <summary>判断当前排位数据是否包含有效段位。</summary>
    public static bool HasRank(RankedQueue? queue) =>
        queue != null &&
        !string.IsNullOrWhiteSpace(queue.Tier) &&
        !string.Equals(queue.Tier, "NONE", StringComparison.OrdinalIgnoreCase);

    /// <summary>取得排位数据对应的段位名称。</summary>
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

/// <summary>选人伴随窗的排位文案；根据界面语言转换客户端的英文段位代码。</summary>
public static class RankedDisplayFormatter
{
    /// <summary>将排位概况格式化为紧凑展示文本。</summary>
    public static string FormatCompact(RankedQueue? queue, bool english)
    {
        if (!RankedDisplayRules.HasRank(queue)) return english ? "Unranked" : "未定级";
        string tier = english ? queue!.Tier.ToUpperInvariant() : ToChineseTier(queue!.Tier);
        string division = queue.Tier.ToUpperInvariant() is "MASTER" or "GRANDMASTER" or "CHALLENGER"
            ? "" : english ? queue.Division : ToChineseDivision(queue.Division);
        return string.IsNullOrWhiteSpace(division) ? tier : english ? $"{tier} {division}" : $"{tier}{division}";
    }

    /// <summary>将单双排数据格式化为展示文本。</summary>
    public static string FormatSoloQueue(RankedQueue? queue, bool english)
    {
        if (!RankedDisplayRules.HasRank(queue))
            return english ? "Rank unavailable" : "段位未获取";

        string tier = english ? queue!.Tier.ToUpperInvariant() : ToChineseTier(queue!.Tier);
        string division = queue.Tier.ToUpperInvariant() is "MASTER" or "GRANDMASTER" or "CHALLENGER"
            ? ""
            : english ? queue.Division : ToChineseDivision(queue.Division);
        string rank = string.IsNullOrWhiteSpace(division) ? tier :
            english ? $"{tier} {division}" : $"{tier}{division}";
        return english
            ? $"Solo/Duo {rank} · {queue.LeaguePoints} LP"
            : $"单双排 {rank} · {queue.LeaguePoints} 胜点";
    }

    /// <summary>将段位等级标识转换为中文名称。</summary>
    private static string ToChineseTier(string? tier) => tier?.Trim().ToUpperInvariant() switch
    {
        "IRON" => "黑铁",
        "BRONZE" => "青铜",
        "SILVER" => "白银",
        "GOLD" => "黄金",
        "PLATINUM" => "铂金",
        "EMERALD" => "翡翠",
        "DIAMOND" => "钻石",
        "MASTER" => "超凡大师",
        "GRANDMASTER" => "宗师",
        "CHALLENGER" => "最强王者",
        _ => tier ?? ""
    };

    /// <summary>将段位小级别转换为中文显示文本。</summary>
    private static string ToChineseDivision(string? division) => division?.Trim().ToUpperInvariant() switch
    {
        "I" => "一",
        "II" => "二",
        "III" => "三",
        "IV" => "四",
        _ => division ?? ""
    };
}
