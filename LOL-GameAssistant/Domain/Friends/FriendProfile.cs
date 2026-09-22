namespace LOL_GameAssistant.Domain.Friends;

/// <summary>好友在线状态的领域枚举，避免表现层依赖 LCU 的原始字符串。</summary>
public enum FriendPresence
{
    Offline,
    Online,
    Away,
    DoNotDisturb,
    Mobile,
    InGame,
    Spectating,
    Unknown
}

/// <summary>
/// 好友领域模型。
/// 仅保留好友列表和观战所需的信息，LCU 的 JSON 字段由基础设施层负责适配。
/// </summary>
public sealed record FriendProfile(
    string? Puuid,
    string DisplayName,
    FriendPresence Presence,
    string? StatusMessage,
    int ProfileIconId,
    string? GameQueueType,
    long? ActiveGameId)
{
    /// <summary>是否为可显示的在线状态。</summary>
    public bool IsOnline => Presence is not (FriendPresence.Offline or FriendPresence.Unknown);

    /// <summary>LCU 返回有效对局 ID 且好友正在游戏中时，才允许出现观战操作。</summary>
    public bool CanSpectate => Presence == FriendPresence.InGame && ActiveGameId is > 0;
}