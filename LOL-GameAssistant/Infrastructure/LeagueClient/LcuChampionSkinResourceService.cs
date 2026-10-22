using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.Skins;
using Newtonsoft.Json.Linq;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>直接读取客户端选肤资源，不用模型名猜测 CDN 图片地址。</summary>
public sealed class LcuChampionSkinResourceService(ILcuRequestSender lcu, Func<string?>? version = null) : IChampionSkinResourceService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, (DateTime Expires, IReadOnlyList<ChampionSkinResource> Skins)> _catalogs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GameAsset> _posters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _posterOrder = new();

    public async Task<IReadOnlyList<ChampionSkinResource>> GetCatalogAsync(string model, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model)) return [];
        string key = $"{version?.Invoke()}:{model}";
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_catalogs.TryGetValue(key, out var cached) && cached.Expires > DateTime.UtcNow) return cached.Skins;
            var summary = Parse(await lcu.GetStringAsync("/lol-game-data/assets/v1/champion-summary.json", cancellationToken).ConfigureAwait(false));
            int id = summary?.Children<JObject>().FirstOrDefault(champion => string.Equals((string?)champion["alias"], model, StringComparison.OrdinalIgnoreCase))?.Value<int>("id") ?? 0;
            if (id <= 0) return [];
            var champion = Parse(await lcu.GetStringAsync($"/lol-game-data/assets/v1/champions/{id}.json", cancellationToken).ConfigureAwait(false));
            IEnumerable<JObject> skins = champion?["skins"]?.Children<JObject>() ?? [];
            if (!skins.Any())
            {
                var all = Parse(await lcu.GetStringAsync("/lol-game-data/assets/v1/skins.json", cancellationToken).ConfigureAwait(false));
                skins = (all is JObject map ? map.Properties().Select(p => p.Value).OfType<JObject>() : all?.Children<JObject>() ?? [])
                    .Where(skin => (skin.Value<int?>("id") ?? 0) / 1000 == id);
            }
            var result = new List<ChampionSkinResource>();
            foreach (var skin in skins)
            {
                int fullId = skin.Value<int?>("id") ?? -1;
                if (fullId < 0 || fullId / 1000 != id) continue;
                var paths = ReadPaths(skin);
                var quality = ReadQuality(skin.Value<string>("rarity"), skin.Value<bool?>("isBase") == true || fullId % 1000 == 0);
                result.Add(new(fullId % 1000, skin.Value<string>("name") ?? model, paths, quality));
                foreach (var chroma in skin["chromas"]?.Children<JObject>() ?? [])
                {
                    int chromaId = chroma.Value<int?>("id") ?? -1;
                    if (chromaId < 0 || chromaId / 1000 != id) continue;
                    result.Add(new(chromaId % 1000, chroma.Value<string>("name") ?? skin.Value<string>("name") ?? model,
                        ReadPaths(chroma).Concat(paths).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                        quality, true));
                }
            }
            IReadOnlyList<ChampionSkinResource> catalog = result.DistinctBy(skin => skin.SkinNumber).ToArray();
            // 连接失败和空目录不缓存，重新读取英雄时仍可立即恢复。
            if (catalog.Count > 0) _catalogs[key] = (DateTime.UtcNow.AddMinutes(10), catalog);
            return catalog;
        }
        finally { _gate.Release(); }
    }

    public async Task<GameAsset?> GetPosterAsync(ChampionSkinResource skin, CancellationToken cancellationToken = default)
    {
        foreach (string path in skin.PosterPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var endpoint = NormalizeAssetPath(path);
            if (endpoint == null) continue;
            string key = $"{version?.Invoke()}:{endpoint}";
            lock (_posters) { if (_posters.TryGetValue(key, out var cached)) return cached; }
            var bytes = await lcu.GetBytesAsync(endpoint, cancellationToken).ConfigureAwait(false);
            if (!IsImage(bytes)) continue;
            var image = new GameAsset(bytes!);
            lock (_posters)
            {
                if (!_posters.ContainsKey(key))
                {
                    while (_posterOrder.Count >= 8) _posters.Remove(_posterOrder.Dequeue());
                    _posters[key] = image; _posterOrder.Enqueue(key);
                }
            }
            return image;
        }
        return null;
    }

    private static JToken? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JToken.Parse(json); } catch (Newtonsoft.Json.JsonException) { return null; }
    }
    internal static SkinQuality ReadQuality(string? rarity, bool isBase) => isBase ? SkinQuality.Base : rarity?.Trim().ToLowerInvariant() switch
    {
        "knorarity" => SkinQuality.Standard,
        "kepic" => SkinQuality.Epic,
        "klegendary" => SkinQuality.Legendary,
        "kultimate" => SkinQuality.Ultimate,
        "kmythic" => SkinQuality.Mythic,
        "kexalted" => SkinQuality.Exalted,
        "ktranscendent" => SkinQuality.Transcendent,
        _ => SkinQuality.Unknown
    };
    private static string[] ReadPaths(JObject skin) => new[] { "splashPath", "uncenteredSplashPath", "chromaPath", "tilePath", "loadScreenPath" }
        .Select(field => NormalizeAssetPath(skin.Value<string>(field))).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    internal static string? NormalizeAssetPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        path = path.Replace('\\', '/');
        if (path.Contains("..", StringComparison.Ordinal) || path.Contains(':') || path.Contains('?') || path.Contains('#')) return null;
        if (path.StartsWith("/lol-game-data/assets/", StringComparison.OrdinalIgnoreCase)) return path;
        if (path.StartsWith("/ASSETS/", StringComparison.OrdinalIgnoreCase)) return "/lol-game-data/assets" + path;
        return null;
    }
    private static bool IsImage(byte[]? bytes) => bytes is { Length: >= 12 } &&
        ((bytes[0] == 0xff && bytes[1] == 0xd8) || (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4e && bytes[3] == 0x47) ||
         (bytes[0] == 'R' && bytes[1] == 'I' && bytes[8] == 'W' && bytes[9] == 'E'));
}
