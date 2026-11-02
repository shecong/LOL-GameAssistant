using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Helper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LOL_GameAssistant.Infrastructure.Skins;

/// <summary>读取 OP.GG 公开皮肤统计页，与其“最佳皮肤排行”一致按 play 排序。</summary>
public sealed class OpggSkinPopularityService(HttpClient? client = null) : ISkinPopularityService
{
    private static readonly HttpClient Shared = new(NetworkTrafficService.Handler(NetworkModule.Assets,
        new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }))
    { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 4 * 1024 * 1024 };
    private readonly HttpClient _http = client ?? Shared;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, (DateTime Expires, IReadOnlyList<SkinUsageStatistics> Statistics)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<SkinUsageStatistics>> GetStatisticsAsync(string model, CancellationToken cancellationToken = default)
    {
        if (!Regex.IsMatch(model, "^[A-Za-z0-9]+$", RegexOptions.CultureInvariant)) return [];
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(model, out var cached) && cached.Expires > DateTime.UtcNow) return cached.Statistics;
            // The public page may reject bare HTTP/1.1 requests. Negotiate HTTP/2 and
            // accept compression to avoid downloading the large Next.js stream verbatim.
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://op.gg/zh-cn/lol/skins/statistics/{model.ToLowerInvariant()}")
            { Version = HttpVersion.Version20, VersionPolicy = HttpVersionPolicy.RequestVersionOrLower };
            request.Headers.UserAgent.ParseAdd("sc-Assistant/1.0");
            request.Headers.Accept.ParseAdd("text/html");
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            string page = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var statistics = ParseStatistics(page, model);
            if (statistics.Count > 0) _cache[model] = (DateTime.UtcNow.AddHours(6), statistics);
            return statistics;
        }
        finally { _gate.Release(); }
    }

    internal static IReadOnlyList<SkinUsageStatistics> ParseStatistics(string page, string model)
    {
        // Next.js streams JSON strings in multiple script tags. Decode and join the data chunks,
        // without executing JavaScript, before reading the table's JSON arrays.
        var payload = new StringBuilder();
        foreach (Match chunk in Regex.Matches(page, """self\.__next_f\.push\(\[1,("(?:\\.|[^"\\])*")\]\)""", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)))
        {
            try { payload.Append(JsonConvert.DeserializeObject<string>(chunk.Groups[1].Value)); }
            catch (JsonException) { }
        }
        string data = payload.ToString();
        var result = new List<SkinUsageStatistics>();
        foreach (Match array in Regex.Matches(data, "\"data\"\\s*:\\s*\\[", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            JArray rows;
            try
            {
                using var text = new StringReader(data[(array.Index + array.Length - 1)..]);
                using var reader = new JsonTextReader(text) { MaxDepth = 64 };
                rows = JArray.Load(reader);
            }
            catch (JsonException) { continue; }
            foreach (var row in rows.OfType<JObject>())
            {
                if (!string.Equals((string?)row["champion_key"], model, StringComparison.OrdinalIgnoreCase) || row["is_base"]?.Type != JTokenType.Boolean || (bool)row["is_base"]!) continue;
                long? Integer(JToken? token) => token?.Type == JTokenType.Integer && long.TryParse(token.ToString(), out long number) ? number : null;
                var id = Integer(row["skin_id"]); var total = Integer(row["total_play"]); var plays = Integer(row["play"]);
                if (id is not > 0 or > int.MaxValue || total is not > 0 || plays is not > 0 || plays > total || id % 1000 == 0) continue;
                result.Add(new((int)(id.Value % 1000), plays.Value, 100d * plays.Value / total.Value));
                foreach (var chroma in row["chromas"]?.OfType<JObject>() ?? [])
                {
                    var chromaId = Integer(chroma["chroma_id"]); var chromaPlays = Integer(chroma["play"]);
                    if (chromaId is not > 0 or > int.MaxValue || chromaId / 1000 != id / 1000 || chromaPlays is not > 0 || chromaPlays > plays) continue;
                    // Chroma pick_rate on OP.GG is relative to its parent skin. Normalize to all
                    // games on this champion so chromas and parent skins remain comparable.
                    result.Add(new((int)(chromaId.Value % 1000), chromaPlays.Value, 100d * chromaPlays.Value / total.Value));
                }
            }
        }
        return result.DistinctBy(r => r.SkinNumber).OrderByDescending(r => r.Plays).ToArray();
    }
}
