namespace LOL_GameAssistant.Domain.ChampionSelect;

/// <summary>选人阶段的稳定快照，屏蔽 LCU 原始 JSON 结构。</summary>
public sealed class ChampionSelectionSnapshot
{
    public IReadOnlyList<IReadOnlyList<ChampionSelectionAction>> Actions { get; init; } =
        Array.Empty<IReadOnlyList<ChampionSelectionAction>>();

    public IReadOnlyList<ChampionSelectionMember> MyTeam { get; init; } = Array.Empty<ChampionSelectionMember>();
    public IReadOnlyList<ChampionSelectionMember> TheirTeam { get; init; } = Array.Empty<ChampionSelectionMember>();
    public int LocalPlayerCellId { get; init; }
}

/// <summary>选人或禁用动作。</summary>
public sealed class ChampionSelectionAction
{
    public int ActorCellId { get; init; }
    public int ChampionId { get; init; }
    public bool IsAllyAction { get; init; }
    public bool IsInProgress { get; init; }
    public bool Completed { get; init; }
    public string Type { get; init; } = "";
}

/// <summary>选人阶段的队伍成员。</summary>
public sealed class ChampionSelectionMember
{
    public int CellId { get; init; }
    public int ChampionId { get; init; }
    public string AssignedPosition { get; init; } = "";
    public string Puuid { get; init; } = "";
}
