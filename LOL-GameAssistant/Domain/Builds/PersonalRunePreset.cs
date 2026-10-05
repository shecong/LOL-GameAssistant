namespace LOL_GameAssistant.Domain.Builds;

/// <summary>用户保存的选人符文与召唤师技能方案。</summary>
public sealed class PersonalRunePreset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public int ChampionId { get; set; }
    public string Mode { get; set; } = "ranked";
    public string Position { get; set; } = "mid";
    public int Priority { get; set; }
    public bool AutoApply { get; set; }
    public int PrimaryStyleId { get; set; }
    public int SubStyleId { get; set; }
    public List<int> RunePerkIds { get; set; } = new();
    public List<int> SummonerSpellIds { get; set; } = new();

    /// <summary>检查符文方案是否包含应用所需的有效字段。</summary>
    public bool IsValid => ChampionId > 0 && PrimaryStyleId > 0 && SubStyleId > 0 &&
        RunePerkIds?.Count >= 6 && SummonerSpellIds?.Count == 2 &&
        SummonerSpellIds.All(id => id > 0);
}

/// <summary>按英雄、模式和分路解析优先使用的个人符文方案。</summary>
public static class PersonalRunePresetResolver
{
    /// <summary>查找匹配当前英雄和场景的个人符文方案。</summary>
    public static PersonalRunePreset? Find(
        IEnumerable<PersonalRunePreset> presets, int championId, string mode, string? position)
    {
        string role = NormalizePosition(position);
        // 峡谷方案精确匹配分路，其他模式只匹配英雄和模式；多个方案按用户优先级选取。
        return presets.Where(preset => preset.IsValid && preset.AutoApply &&
                preset.ChampionId == championId &&
                string.Equals(preset.Mode, mode, StringComparison.OrdinalIgnoreCase) &&
                (mode != "ranked" || string.Equals(preset.Position, role, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(preset => preset.Priority)
            .ThenBy(preset => preset.Name, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>将分路别名归一化，供方案选择和推荐匹配使用。</summary>
    public static string NormalizePosition(string? position) => position?.Trim().ToUpperInvariant() switch
    {
        "TOP" => "top",
        "JUNGLE" or "JNG" => "jungle",
        "MIDDLE" or "MID" => "mid",
        "BOTTOM" or "BOT" or "ADC" => "adc",
        "UTILITY" or "SUPPORT" or "SUP" => "support",
        _ => "unknown"
    };
}
