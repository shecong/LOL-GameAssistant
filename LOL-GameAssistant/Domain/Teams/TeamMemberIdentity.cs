namespace LOL_GameAssistant.Domain.Teams;

/// <summary>用于队伍关系分析的最小玩家标识。</summary>
public sealed record TeamMemberIdentity(string Puuid, string Name);
