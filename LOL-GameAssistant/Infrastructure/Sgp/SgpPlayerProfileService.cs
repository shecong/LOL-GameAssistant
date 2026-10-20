using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Domain.Players;
using Newtonsoft.Json.Linq;

namespace LOL_GameAssistant.Infrastructure.Sgp;

/// <summary>SGP 提供等级头像；当前登录身份及 Riot ID 解析仍由本机客户端权威提供。</summary>
public sealed class SgpPlayerProfileService(SgpClient client, IPlayerProfileService identities,
    LOL_GameAssistant.Application.Matches.IMatchHistoryService? history = null) : IPlayerProfileService
{
    public void ResetClientContext() => identities.ResetClientContext();
    public Task<PlayerProfile?> GetCurrentAsync(CancellationToken cancellationToken = default) => identities.GetCurrentAsync(cancellationToken);

    public async Task<PlayerProfile?> GetByPuuidAsync(string puuid, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(puuid)) return null;
        SgpContext context;
        try { context = await client.GetContextAsync(cancellationToken).ConfigureAwait(false); }
        catch (NotSupportedException) when (history == null)
        { return await identities.GetByPuuidAsync(puuid, cancellationToken).ConfigureAwait(false); }
        if (context.IsCrossRegion)
        {
            var page = await (history ?? new SgpMatchHistoryService(client)).GetPageAsync(puuid, 0, 0, cancellationToken).ConfigureAwait(false);
            var player = page?.Games?.Games.FirstOrDefault()?.ParticipantIdentities.FirstOrDefault(p => p.Player?.Puuid == puuid)?.Player;
            // Never substitute the logged-in realm's identity, icon or level for another realm.
            return new PlayerProfile(puuid, string.IsNullOrWhiteSpace(player?.GameName) ? "跨区玩家（暂无可读战绩）" : player.GameName,
                player?.TagLine ?? "", 0, 0, 0, 0);
        }
        return await EnrichAsync(await identities.GetByPuuidAsync(puuid, cancellationToken).ConfigureAwait(false), puuid, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PlayerProfile?> FindByRiotIdAsync(string gameName, string tagLine, CancellationToken cancellationToken = default)
    {
        if ((await client.GetContextAsync(cancellationToken).ConfigureAwait(false)).IsCrossRegion)
            throw new NotSupportedException("跨区名称搜索尚不支持，请输入目标大区玩家的 PUUID。");
        var identity = await identities.FindByRiotIdAsync(gameName, tagLine, cancellationToken).ConfigureAwait(false);
        return identity == null ? null : await EnrichAsync(identity, identity.Puuid, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PlayerProfile?> FindByNameAsync(string gameName, CancellationToken cancellationToken = default)
    {
        if ((await client.GetContextAsync(cancellationToken).ConfigureAwait(false)).IsCrossRegion)
            throw new NotSupportedException("跨区名称搜索尚不支持，请输入目标大区玩家的 PUUID。");
        var identity = await identities.FindByNameAsync(gameName, cancellationToken).ConfigureAwait(false);
        return identity == null ? null : await EnrichAsync(identity, identity.Puuid, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PlayerProfile?> EnrichAsync(PlayerProfile? identity, string puuid, CancellationToken token)
    {
        try
        {
            var context = await client.GetContextAsync(token).ConfigureAwait(false);
            var result = await client.ReadAsync(context, $"/summoner-ledge/v1/regions/{context.Platform}/summoners/puuids", true, token, new JArray(puuid)).ConfigureAwait(false);
            var profile = result is JArray rows ? rows.FirstOrDefault(p => p.Value<string>("puuid") == puuid) : result;
            if (profile?.Value<string>("puuid") != puuid) throw new IOException("SGP 玩家标识不匹配。");
            // Tencent often returns an empty name. Never replace a real Riot ID with that field.
            return new PlayerProfile(puuid, identity?.GameName ?? profile.Value<string>("name") ?? "未知玩家", identity?.TagLine ?? "",
                profile.Value<int?>("profileIconId") ?? identity?.ProfileIconId ?? 0,
                profile.Value<int?>("level") ?? identity?.SummonerLevel ?? 0,
                profile.Value<int?>("expPoints") ?? identity?.XpSinceLastLevel ?? 0,
                profile.Value<int?>("expToNextLevel") ?? identity?.XpUntilNextLevel ?? 0);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or NotSupportedException or Newtonsoft.Json.JsonException)
        { return identity; }
    }
}
