namespace LOL_GameAssistant.Domain.Skins;

/// <summary>Choices observed in the supported game's loading-time crest lookup. Native code validates again before writing.</summary>
public static class RankBorderPresets
{
    public const string Profile = "cn16.21.regalia.v2";
    public static IReadOnlyList<RankBorderEntry> Entries { get; } = new (string Id, string Name)[] {
        ("IRON", "黑铁"), ("BRONZE", "青铜"), ("SILVER", "白银"), ("GOLD", "黄金"), ("PLATINUM", "铂金"),
        ("EMERALD", "翡翠"), ("DIAMOND", "钻石"), ("MASTER", "超凡大师"), ("GRANDMASTER", "傲世宗师"), ("CHALLENGER", "最强王者")
    }.Select(entry => new RankBorderEntry { Id = entry.Id, Name = entry.Name }).ToArray();
    public static bool IsValid(string? id) => Entries.Any(entry => entry.Id == id);
}
