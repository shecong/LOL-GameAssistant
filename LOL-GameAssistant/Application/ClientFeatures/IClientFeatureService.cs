namespace LOL_GameAssistant.Application.ClientFeatures;

/// <summary>
/// Sona 类增强功能的外置实现端口。
/// 所有操作仅面向用户本机已经登录的 LCU；界面层不接触端点、认证或 JSON 结构。
/// </summary>
public interface IClientFeatureService
{
    /// <summary>向客户端提交拒绝当前匹配确认的请求。</summary>
    Task<ClientFeatureResult> DeclineReadyCheckAsync(CancellationToken cancellationToken = default);

    /// <summary>向客户端提交退出当前选人阶段的请求。</summary>
    Task<ClientFeatureResult> DodgeChampionSelectAsync(CancellationToken cancellationToken = default);

    /// <summary>请求交换指定的大乱斗备选英雄。</summary>
    Task<ClientFeatureResult> SwapAramBenchAsync(int championId, CancellationToken cancellationToken = default);

    /// <summary>根据指定队列创建快捷大厅。</summary>
    Task<ClientFeatureResult> CreateQuickLobbyAsync(int queueId, CancellationToken cancellationToken = default);

    /// <summary>请求从当前流程返回大厅。</summary>
    Task<ClientFeatureResult> ReturnToLobbyAsync(bool startMatchmaking, CancellationToken cancellationToken = default);

    /// <summary>从可点赞队友中选择目标并提交点赞。</summary>
    Task<ClientFeatureResult> HonorRandomEligibleAllyAsync(CancellationToken cancellationToken = default);

    /// <summary>请求下载指定对局录像。</summary>
    Task<ClientFeatureResult> DownloadReplayAsync(long gameId, CancellationToken cancellationToken = default);

    /// <summary>请求观看指定对局录像。</summary>
    Task<ClientFeatureResult> WatchReplayAsync(long gameId, CancellationToken cancellationToken = default);

    /// <summary>读取待领取奖励列表。</summary>
    Task<IReadOnlyList<ClientRewardGrant>> GetPendingRewardsAsync(CancellationToken cancellationToken = default);

    /// <summary>逐项领取当前可领取的奖励。</summary>
    Task<ClientFeatureResult> ClaimAllPendingRewardsAsync(CancellationToken cancellationToken = default);

    /// <summary>列出当前账号已有的游戏设置备份。</summary>
    Task<IReadOnlyList<ClientSettingsBackup>> ListGameSettingsBackupsAsync(CancellationToken cancellationToken = default);

    /// <summary>按指定名称保存当前游戏设置备份。</summary>
    Task<ClientFeatureResult> SaveGameSettingsBackupAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>从所选备份恢复游戏设置。</summary>
    Task<ClientFeatureResult> RestoreGameSettingsBackupAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>删除指定的游戏设置备份。</summary>
    Task<ClientFeatureResult> DeleteGameSettingsBackupAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>读取好友在线及游戏活动信息。</summary>
    Task<IReadOnlyList<FriendActivity>> GetFriendActivitiesAsync(CancellationToken cancellationToken = default);

    /// <summary>更新客户端聊天的在线状态。</summary>
    Task<ClientFeatureResult> UpdateChatPresenceAsync(string availability, string statusMessage, CancellationToken cancellationToken = default);

    /// <summary>读取当前账号客户端库存中的皮肤条目，供可视化背景选择器使用。</summary>
    Task<IReadOnlyList<ClientSkinChoice>> GetProfileBackgroundChoicesAsync(CancellationToken cancellationToken = default);

    /// <summary>从本机客户端的英雄库存读取皮肤名称与缩略图，用于设置页的可视化预览。</summary>
    Task<ClientSkinPreview?> GetProfileSkinPreviewAsync(long skinId, CancellationToken cancellationToken = default);

    /// <summary>更新当前召唤师的生涯背景。</summary>
    Task<ClientFeatureResult> UpdateProfileBackgroundAsync(long skinId, CancellationToken cancellationToken = default);

    /// <summary>更新当前召唤师头像。</summary>
    Task<ClientFeatureResult> SetProfileIconAsync(int profileIconId, CancellationToken cancellationToken = default);

    /// <summary>清除当前生涯展示的挑战徽章。</summary>
    Task<ClientFeatureResult> ClearChallengeBadgesAsync(CancellationToken cancellationToken = default);
}

/// <summary>客户端功能操作的成功状态及说明。</summary>
public sealed record ClientFeatureResult(bool Succeeded, string Message)
{
    /// <summary>构建成功结果，向调用方返回操作状态。</summary>
    public static ClientFeatureResult Success(string message) => new(true, message);
    /// <summary>构建失败结果，向调用方保留可展示的原因。</summary>
    public static ClientFeatureResult Failure(string message) => new(false, message);
}

/// <summary>待领取奖励的业务展示信息。</summary>
public sealed record ClientRewardGrant(
    string GrantId,
    string Title,
    int AvailableChoices,
    IReadOnlyList<string> RewardIds);

/// <summary>游戏设置备份的标识及展示信息。</summary>
public sealed record ClientSettingsBackup(string Name, DateTimeOffset CreatedAt);

/// <summary>可用于生涯背景的皮肤条目，不把 LCU 的原始 JSON 暴露给界面。</summary>
public sealed record ClientSkinChoice(long SkinId, string Name, string ChampionName, bool IsOwned);

/// <summary>客户端已识别皮肤的展示信息；图片仍由表现层负责解码。</summary>
public sealed record ClientSkinPreview(long SkinId, string Name, string ChampionName, byte[]? ImageBytes);

/// <summary>好友在线状态和当前游戏活动信息。</summary>
public sealed record FriendActivity(
    string Puuid,
    string DisplayName,
    string Availability,
    string GameStatus,
    int QueueId,
    string QueueName,
    DateTimeOffset? StartedAt)
{
    /// <summary>返回当前操作已经耗费的时间。</summary>
    public TimeSpan? Elapsed => StartedAt is { } started ? DateTimeOffset.UtcNow - started : null;
}