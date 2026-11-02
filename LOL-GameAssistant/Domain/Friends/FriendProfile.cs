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

    /// <summary>有 PUUID 且好友正在 LOL 对局中时，可让客户端尝试观战；聊天数据不一定包含对局 ID。</summary>
    public bool CanSpectate => Presence == FriendPresence.InGame && !string.IsNullOrWhiteSpace(Puuid);
}
