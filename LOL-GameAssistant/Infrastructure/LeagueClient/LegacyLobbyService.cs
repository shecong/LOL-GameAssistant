using LOL_GameAssistant.Application.Lobby;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Entity;
using LOL_GameAssistant.LoLApi;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>旧大厅 LCU 实现的基础设施适配器。</summary>
public sealed class LegacyLobbyService : ILobbyService
{
    /// <summary>向客户端提交开始匹配的请求。</summary>
    public Task StartMatchmakingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Game_Api.OpenGameServer();
    }

    /// <summary>向客户端提交接受匹配确认的请求。</summary>
    public Task AcceptReadyCheckAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Game_Api.GameTrueServer();
    }

    /// <summary>读取当前大厅及成员信息。</summary>
    public async Task<LobbySnapshot?> GetLobbyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LobbyGameInfo? legacy = await Game_Api.GameNowServer().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (legacy == null) return null;

        // 普通大厅的 partyId 属于 members；自定义房间不能用它推断双方预组队。
        string partyId = legacy.GameConfig?.IsCustom == true ? "" : legacy.PartyId ?? "";
        IReadOnlyList<GameTeamMember> partyMembers = MapLobbyMembers(legacy.Members, partyId);
        IReadOnlyList<GameTeamMember> team100 = MapLobbyMembers(legacy.GameConfig?.CustomTeam100, partyId);
        IReadOnlyList<GameTeamMember> team200 = MapLobbyMembers(legacy.GameConfig?.CustomTeam200, partyId);
        return new LobbySnapshot
        {
            IsCustom = legacy.GameConfig?.IsCustom != false,
            GameMode = legacy.GameConfig?.GameMode ?? "",
            QueueId = legacy.GameConfig?.QueueId ?? 0,
            PartyId = partyId,
            LocalPlayerPuuid = legacy.LocalMember?.Puuid ?? "",
            LocalPrimaryPosition = legacy.LocalMember?.FirstPositionPreference ?? "",
            LocalSecondaryPosition = legacy.LocalMember?.SecondPositionPreference ?? "",
            PartyMembers = partyMembers,
            // 普通匹配大厅通常只给 members，不给 customTeam100。
            Team100 = team100.Count == 0 && team200.Count == 0 && partyId.Length > 0
                ? partyMembers : team100,
            Team200 = team200
        };
    }

    /// <summary>读取客户端当前游戏流程阶段。</summary>
    public Task<string?> GetGameFlowPhaseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Game_Api.GameFlowPhaseServer();
    }

    /// <summary>读取当前游戏会话的队伍和英雄信息。</summary>
    public async Task<ActiveGameSnapshot?> GetCurrentSessionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GameSessionResponse? legacy = await Game_Api.GameLineInfoServer(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return legacy == null ? null : new ActiveGameSnapshot
        {
            Phase = legacy.Phase,
            GameMode = !string.IsNullOrWhiteSpace(legacy.GameData?.GameMode)
                ? legacy.GameData.GameMode : legacy.GameData?.Queue?.GameMode ?? "",
            QueueId = legacy.GameData?.QueueId is > 0
                ? legacy.GameData.QueueId : legacy.GameData?.Queue?.Id ?? 0,
            TeamOne = MapActiveMembers(legacy.GameData?.TeamOne),
            TeamTwo = MapActiveMembers(legacy.GameData?.TeamTwo)
        };
    }

    /// <summary>大厅 DTO 仅在此处转换为应用可消费的队伍成员快照。</summary>
    private static IReadOnlyList<GameTeamMember> MapLobbyMembers(IEnumerable<Member>? members, string partyId)
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
                IsBot = member.IsBot,
                PartyId = partyId
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
                IsBot = false,
                PartyId = member.PartyId,
                TeamParticipantId = member.TeamParticipantId
            }).ToList();
    }
}
