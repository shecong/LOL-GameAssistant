using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Domain.Players;
using LOL_GameAssistant.Infrastructure.LeagueClient.Dto;
using Newtonsoft.Json;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>通过 LCU 查询召唤师资料，并把不稳定的 JSON 字段适配为领域模型。</summary>
public sealed class LcuPlayerProfileService : IPlayerProfileService
{
    private readonly ILcuRequestSender _requestSender;

    public LcuPlayerProfileService(ILcuRequestSender requestSender)
    {
        _requestSender = requestSender;
    }

    public Task<PlayerProfile?> GetCurrentAsync(CancellationToken cancellationToken = default) =>
        GetAsync("/lol-summoner/v1/current-summoner", cancellationToken);

    public Task<PlayerProfile?> GetByPuuidAsync(string puuid, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(puuid)) return Task.FromResult<PlayerProfile?>(null);
        return GetAsync($"/lol-summoner/v2/summoners/puuid/{Uri.EscapeDataString(puuid)}", cancellationToken);
    }

    public async Task<PlayerProfile?> FindByRiotIdAsync(string gameName, string tagLine, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gameName) || string.IsNullOrWhiteSpace(tagLine)) return null;
        string escapedName = Uri.EscapeDataString(gameName.Trim());
        string escapedTag = Uri.EscapeDataString(tagLine.Trim());

        // 国服不同客户端版本对三种查询路由的支持不同，按兼容顺序回退。
        return await GetAsync($"/lol-summoner/v1/summoners?name={escapedName}&tagLine={escapedTag}", cancellationToken).ConfigureAwait(false)
            ?? await GetAsync($"/lol-summoner/v1/summoners/by-name/{escapedName}/{escapedTag}", cancellationToken).ConfigureAwait(false)
            ?? await GetAsync($"/lol-summoner/v1/summoners/by-name/{escapedName}", cancellationToken).ConfigureAwait(false);
    }

    private async Task<PlayerProfile?> GetAsync(string endpoint, CancellationToken cancellationToken)
    {
        string? json = await _requestSender.GetStringAsync(endpoint, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return null;
        var dto = JsonConvert.DeserializeObject<LcuPlayerProfileDto>(json);
        if (dto == null || string.IsNullOrWhiteSpace(dto.Puuid)) return null;

        return new PlayerProfile(
            dto.Puuid,
            dto.GameName?.Trim() ?? "未知玩家",
            dto.TagLine?.Trim().TrimStart('#') ?? "",
            ParseNonNegative(dto.ProfileIconId),
            ParseNonNegative(dto.SummonerLevel),
            Math.Max(0, dto.XpSinceLastLevel),
            Math.Max(0, dto.XpUntilNextLevel));
    }

    private static int ParseNonNegative(string? value) =>
        int.TryParse(value, out int parsed) ? Math.Max(0, parsed) : 0;
}