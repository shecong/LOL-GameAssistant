namespace LOL_GameAssistant.Domain.LeagueClient;

/// <summary>大厅或进行中对局中可展示的一名成员。</summary>
public sealed class GameTeamMember
{
    public string Puuid { get; init; } = "";
    public string SummonerName { get; init; } = "";
    public int ChampionId { get; init; }
    public string Position { get; init; } = "";
    public string SecondaryPosition { get; init; } = "";
    public bool IsBot { get; init; }
    /// <summary>成员所属房间；部分 LCU 会话阶段不会返回。</summary>
    public string PartyId { get; init; } = "";
    /// <summary>对局会话提供的参与者标识，不能单独作为本局组队依据。</summary>
    public int TeamParticipantId { get; init; }
}

/// <summary>大厅阶段的队列和分队快照。</summary>
public sealed class LobbySnapshot
{
    public string GameMode { get; init; } = "";
    public int QueueId { get; init; }
    public string PartyId { get; init; } = "";
    public string LocalPlayerPuuid { get; init; } = "";
    public string LocalPrimaryPosition { get; init; } = "";
    public string LocalSecondaryPosition { get; init; } = "";
    public IReadOnlyList<GameTeamMember> PartyMembers { get; init; } = Array.Empty<GameTeamMember>();
    public IReadOnlyList<GameTeamMember> Team100 { get; init; } = Array.Empty<GameTeamMember>();
    public IReadOnlyList<GameTeamMember> Team200 { get; init; } = Array.Empty<GameTeamMember>();
}

/// <summary>实际游戏阶段的双方阵容快照。</summary>
public sealed class ActiveGameSnapshot
{
    public string Phase { get; init; } = "";
    public string GameMode { get; init; } = "";
    public int QueueId { get; init; }
    public IReadOnlyList<GameTeamMember> TeamOne { get; init; } = Array.Empty<GameTeamMember>();
    public IReadOnlyList<GameTeamMember> TeamTwo { get; init; } = Array.Empty<GameTeamMember>();
}
