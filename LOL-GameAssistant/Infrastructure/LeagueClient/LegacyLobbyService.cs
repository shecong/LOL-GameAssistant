using LOL_GameAssistant.Application.Lobby;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Entity;
using LOL_GameAssistant.LoLApi;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>旧大厅 LCU 实现的基础设施适配器。</summary>
public sealed class LegacyLobbyService : ILobbyService
{
    public Task StartMatchmakingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Game_Api.OpenGameServer();
    }

    public Task AcceptReadyCheckAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Game_Api.GameTrueServer();
    }

    public async Task<LobbySnapshot?> GetLobbyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LobbyGameInfo? legacy = await Game_Api.GameNowServer().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return legacy == null ? null : new LobbySnapshot
        {
            GameMode = legacy.GameConfig?.GameMode ?? "",
            QueueId = legacy.GameConfig?.QueueId ?? 0,
            LocalPlayerPuuid = legacy.LocalMember?.Puuid ?? "",
            LocalPrimaryPosition = legacy.LocalMember?.FirstPositionPreference ?? "",
            LocalSecondaryPosition = legacy.LocalMember?.SecondPositionPreference ?? "",
            Team100 = MapLobbyMembers(legacy.GameConfig?.CustomTeam100),
            Team200 = MapLobbyMembers(legacy.GameConfig?.CustomTeam200)
        };
    }

    public Task<string?> GetGameFlowPhaseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Game_Api.GameFlowPhaseServer();
    }

    public async Task<ActiveGameSnapshot?> GetCurrentSessionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GameSessionResponse? legacy = await Game_Api.GameLineInfoServer().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return legacy == null ? null : new ActiveGameSnapshot
        {
            Phase = legacy.Phase,
            TeamOne = MapActiveMembers(legacy.GameData?.TeamOne),
            TeamTwo = MapActiveMembers(legacy.GameData?.TeamTwo)
        };
    }

    /// <summary>大厅 DTO 仅在此处转换为应用可消费的队伍成员快照。</summary>
    private static IReadOnlyList<GameTeamMember> MapLobbyMembers(IEnumerable<Member>? members)
    {
        return members == null
            ? Array.Empty<GameTeamMember>()
            : members.Select(member => new GameTeamMember
            {
                Puuid = member.Puuid,
                SummonerName = member.SummonerName,
                ChampionId = member.IsBot ? member.BotChampionId : 0,
                Position = member.FirstPositionPreference,
                SecondaryPosition = member.SecondPositionPreference,
                IsBot = member.IsBot
            }).ToList();
    }

    /// <summary>进行中对局 DTO 仅在此处转换为应用可消费的队伍成员快照。</summary>
    private static IReadOnlyList<GameTeamMember> MapActiveMembers(IEnumerable<TeamMember>? members)
    {
        return members == null
            ? Array.Empty<GameTeamMember>()
            : members.Select(member => new GameTeamMember
            {
                Puuid = member.Puuid,
                SummonerName = member.SummonerName,
                ChampionId = member.ChampionId,
                Position = member.SelectedPosition,
                SecondaryPosition = "",
                IsBot = false
            }).ToList();
    }
}
