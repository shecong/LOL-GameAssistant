namespace LOL_GameAssistant.Domain.LeagueClient;

/// <summary>大厅或进行中对局中可展示的一名成员。</summary>
public sealed class GameTeamMember
{
    public string Puuid { get; init; } = "";
    public string SummonerName { get; init; } = "";
    public int ChampionId { get; init; }
    public string Position { get; init; } = "";
    public bool IsBot { get; init; }
}

/// <summary>大厅阶段的队列和分队快照。</summary>
public sealed class LobbySnapshot
{
    public string GameMode { get; init; } = "";
    public int QueueId { get; init; }
    public string LocalPlayerPuuid { get; init; } = "";
    public IReadOnlyList<GameTeamMember> Team100 { get; init; } = Array.Empty<GameTeamMember>();
    public IReadOnlyList<GameTeamMember> Team200 { get; init; } = Array.Empty<GameTeamMember>();
}

/// <summary>实际游戏阶段的双方阵容快照。</summary>
public sealed class ActiveGameSnapshot
{
    public string Phase { get; init; } = "";
    public IReadOnlyList<GameTeamMember> TeamOne { get; init; } = Array.Empty<GameTeamMember>();
    public IReadOnlyList<GameTeamMember> TeamTwo { get; init; } = Array.Empty<GameTeamMember>();
}
