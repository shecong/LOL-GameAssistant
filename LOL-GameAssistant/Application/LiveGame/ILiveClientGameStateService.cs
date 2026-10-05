using LOL_GameAssistant.Domain.LiveGame;

namespace LOL_GameAssistant.Application.LiveGame;

/// <summary>读取本机 Live Client Data API 中允许展示的当前玩家状态。</summary>
public interface ILiveClientGameStateService
{
    /// <summary>读取当前玩家的金币、装备及游戏时间状态。</summary>
    Task<LivePlayerState?> GetOwnStateAsync(CancellationToken cancellationToken = default);

    /// <summary>读取当前游戏状态的完整快照。</summary>
    Task<LiveClientGameSnapshot?> GetSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>读取游戏客户端返回的当前玩法模式。</summary>
    Task<string?> GetGameModeAsync(CancellationToken cancellationToken = default);

    /// <summary>读取当前对局已进行的秒数。</summary>
    Task<int?> GetGameTimeSecondsAsync(CancellationToken cancellationToken = default);
}