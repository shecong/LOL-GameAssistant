using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LOL_GameAssistant.Infrastructure.Skins;

/// <summary>Bundled champion search keywords and multi-position filters; no network work while typing.</summary>
public sealed class CslolChampionSearch
{
    private static readonly Lazy<Dictionary<string, Champion>> Champions = new(() =>
    {
        using var stream = typeof(CslolChampionSearch).Assembly.GetManifestResourceStream("LOL_GameAssistant.Resources.CslolChampionSearch.json")
            ?? throw new InvalidOperationException("缺少英雄搜索资源");
        var champions = JsonSerializer.Deserialize<Dictionary<string, Champion>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("英雄搜索资源无效");
        foreach (var champion in champions.Values)
            champion.Keywords = champion.Keywords.Select(Normalize).Distinct().ToArray();
        return new(champions, StringComparer.OrdinalIgnoreCase);
    });

    public bool Matches(string key, string displayName, string query, string position = "")
    {
        Champions.Value.TryGetValue(key, out var champion);
        if (position.Length > 0 && (champion is null || !champion.Positions.Contains(position, StringComparer.OrdinalIgnoreCase))) return false;
        string search = Normalize(query);
        return search.Length == 0 || Normalize(key).Contains(search, StringComparison.Ordinal)
            || Normalize(displayName).Contains(search, StringComparison.Ordinal)
            || champion?.Keywords.Any(keyword => keyword.Contains(search, StringComparison.Ordinal)) == true;
    }

    private static string Normalize(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (char character in value.Normalize(NormalizationForm.FormD))
            if (char.IsLetterOrDigit(character) && CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                result.Append(char.ToLowerInvariant(character));
        return result.ToString();
    }

    private sealed class Champion
    {
        public string[] Keywords { get; set; } = [];
        public string[] Positions { get; set; } = [];
    }
}
