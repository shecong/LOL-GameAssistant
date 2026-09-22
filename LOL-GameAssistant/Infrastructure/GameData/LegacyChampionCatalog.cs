using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.Infrastructure.GameData;

/// <summary>既有英雄映射表的基础设施适配器。</summary>
public sealed class LegacyChampionCatalog : IChampionCatalog
{
    public string GetDisplayName(int championId) =>
        ChampionMap.GetChampion(championId)?.RealName ?? "";

    public IReadOnlyList<ChampionReference> GetAll() =>
        ChampionMap.GetChampionMap()
            .OrderBy(entry => entry.Value.RealName, StringComparer.CurrentCulture)
            .Select(entry => new ChampionReference(entry.Key, entry.Value.RealName))
            .ToArray();

    public int? FindIdByDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return null;
        int id = ChampionMap.GetChampionMap()
            .FirstOrDefault(entry => string.Equals(
                entry.Value.RealName,
                displayName,
                StringComparison.OrdinalIgnoreCase))
            .Key;
        return id > 0 ? id : null;
    }
}