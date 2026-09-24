namespace LOL_GameAssistant.Application.ClientFeatures;

/// <summary>
/// Sona 类增强功能的外置实现端口。
/// 所有操作仅面向用户本机已经登录的 LCU；界面层不接触端点、认证或 JSON 结构。
/// </summary>
public interface IClientFeatureService
{
    Task<ClientFeatureResult> DeclineReadyCheckAsync(CancellationToken cancellationToken = default);
    Task<ClientFeatureResult> DodgeChampionSelectAsync(CancellationToken cancellationToken = default);
    Task<ClientFeatureResult> SwapAramBenchAsync(int championId, CancellationToken cancellationToken = default);
    Task<ClientFeatureResult> CreateQuickLobbyAsync(int queueId, CancellationToken cancellationToken = default);
    Task<ClientFeatureResult> ReturnToLobbyAsync(bool startMatchmaking, CancellationToken cancellationToken = default);
    Task<ClientFeatureResult> HonorRandomEligibleAllyAsync(CancellationToken cancellationToken = default);

    Task<ClientFeatureResult> DownloadReplayAsync(long gameId, CancellationToken cancellationToken = default);
    Task<ClientFeatureResult> WatchReplayAsync(long gameId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClientRewardGrant>> GetPendingRewardsAsync(CancellationToken cancellationToken = default);
    Task<ClientFeatureResult> ClaimAllPendingRewardsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClientSettingsBackup>> ListGameSettingsBackupsAsync(CancellationToken cancellationToken = default);
    Task<ClientFeatureResult> SaveGameSettingsBackupAsync(string name, CancellationToken cancellationToken = default);
    Task<ClientFeatureResult> RestoreGameSettingsBackupAsync(string name, CancellationToken cancellationToken = default);
    Task<ClientFeatureResult> DeleteGameSettingsBackupAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FriendActivity>> GetFriendActivitiesAsync(CancellationToken cancellationToken = default);
    Task<ClientFeatureResult> UpdateChatPresenceAsync(string availability, string statusMessage, CancellationToken cancellationToken = default);
    /// <summary>读取当前账号客户端库存中的皮肤条目，供可视化背景选择器使用。</summary>
    Task<IReadOnlyList<ClientSkinChoice>> GetProfileBackgroundChoicesAsync(CancellationToken cancellationToken = default);
    /// <summary>从本机客户端的英雄库存读取皮肤名称与缩略图，用于设置页的可视化预览。</summary>
    Task<ClientSkinPreview?> GetProfileSkinPreviewAsync(long skinId, CancellationToken cancellationToken = default);
    Task<ClientFeatureResult> UpdateProfileBackgroundAsync(long skinId, CancellationToken cancellationToken = default);
    Task<ClientFeatureResult> SetProfileIconAsync(int profileIconId, CancellationToken cancellationToken = default);
    Task<ClientFeatureResult> ClearChallengeBadgesAsync(CancellationToken cancellationToken = default);
}

public sealed record ClientFeatureResult(bool Succeeded, string Message)
{
    public static ClientFeatureResult Success(string message) => new(true, message);
    public static ClientFeatureResult Failure(string message) => new(false, message);
}

public sealed record ClientRewardGrant(
    string GrantId,
    string Title,
    int AvailableChoices,
    IReadOnlyList<string> RewardIds);

public sealed record ClientSettingsBackup(string Name, DateTimeOffset CreatedAt);

/// <summary>可用于生涯背景的皮肤条目，不把 LCU 的原始 JSON 暴露给界面。</summary>
public sealed record ClientSkinChoice(long SkinId, string Name, string ChampionName, bool IsOwned);

/// <summary>客户端已识别皮肤的展示信息；图片仍由表现层负责解码。</summary>
public sealed record ClientSkinPreview(long SkinId, string Name, string ChampionName, byte[]? ImageBytes);

public sealed record FriendActivity(
    string Puuid,
    string DisplayName,
    string Availability,
    string GameStatus,
    int QueueId,
    string QueueName,
    DateTimeOffset? StartedAt)
{
    public TimeSpan? Elapsed => StartedAt is { } started ? DateTimeOffset.UtcNow - started : null;
}
