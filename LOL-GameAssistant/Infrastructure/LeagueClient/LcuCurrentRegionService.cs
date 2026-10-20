using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Infrastructure.Lounge;
using Newtonsoft.Json.Linq;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>读取当前登录客户端的大区，不根据搜索对象、Riot ID 标签或旧缓存推断。</summary>
public sealed class LcuCurrentRegionService(ILcuRequestSender lcu) : ICurrentRegionService
{
    public async Task<string> GetCurrentRegionAsync(CancellationToken token = default)
    {
        string? raw = await lcu.GetStringAsync("/lol-platform-config/v1/namespaces/LoginDataPacket/platformId", token).ConfigureAwait(false);
        string platform = LoungeLcuReader.ParsePlatform(raw);
        string name = LoungeLcuReader.RegionName(platform);
        if (!name.StartsWith("未知", StringComparison.Ordinal)) return name;
        var current = await ReadObject("/lol-summoner/v1/current-summoner", token).ConfigureAwait(false);
        if (current?.Value<string>("puuid") is not { Length: > 0 } puuid) return "未知大区";
        var me = await ReadObject("/lol-chat/v1/me", token).ConfigureAwait(false);
        return LoungeLcuReader.RegionName(me?.Value<string>("platformId") ?? platform);
    }
    private async Task<JObject?> ReadObject(string route, CancellationToken token)
    {
        string? json = await lcu.GetStringAsync(route, token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JToken.Parse(json) as JObject; } catch (Newtonsoft.Json.JsonException) { return null; }
    }
}
