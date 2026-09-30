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

    public bool IsValid => ChampionId > 0 && PrimaryStyleId > 0 && SubStyleId > 0 &&
        RunePerkIds?.Count >= 6 && SummonerSpellIds?.Count == 2 &&
        SummonerSpellIds.All(id => id > 0);
}

public static class PersonalRunePresetResolver
{
    public static PersonalRunePreset? Find(
        IEnumerable<PersonalRunePreset> presets, int championId, string mode, string? position)
    {
        string role = NormalizePosition(position);
        return presets.Where(preset => preset.IsValid && preset.AutoApply &&
                preset.ChampionId == championId &&
                string.Equals(preset.Mode, mode, StringComparison.OrdinalIgnoreCase) &&
                (mode != "ranked" || string.Equals(preset.Position, role, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(preset => preset.Priority)
            .ThenBy(preset => preset.Name, StringComparer.Ordinal)
            .FirstOrDefault();
    }

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
