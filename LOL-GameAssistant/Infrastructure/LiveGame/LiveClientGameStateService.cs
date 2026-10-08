using LOL_GameAssistant.Application.LiveGame;
using LOL_GameAssistant.Domain.LiveGame;

namespace LOL_GameAssistant.Infrastructure.LiveGame;

/// <summary>本机 Live Client Data API 的基础设施适配器。</summary>
public sealed class LiveClientGameStateService : ILiveClientGameStateService
{
    public Task<IReadOnlyList<LiveScoreboardPlayer>?> GetScoreboardAsync(CancellationToken cancellationToken = default) =>
        LocalLiveClientDataReader.GetScoreboardAsync(cancellationToken);
    /// <summary>读取当前玩家的金币、装备及游戏时间状态。</summary>
    public async Task<LivePlayerState?> GetOwnStateAsync(CancellationToken cancellationToken = default)
    {
        LocalLiveClientOwnState? state = await LocalLiveClientDataReader.GetOwnStateAsync(cancellationToken).ConfigureAwait(false);
        return state == null ? null : new LivePlayerState(state.CurrentGold, state.Items, state.GameTimeSeconds);
    }

    /// <summary>读取当前游戏状态的完整快照。</summary>
    public async Task<LiveClientGameSnapshot?> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        LocalLiveClientSnapshot? snapshot = await LocalLiveClientDataReader.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return snapshot == null
            ? null
            : new LiveClientGameSnapshot(snapshot.CurrentGold, snapshot.Items, snapshot.GameTimeSeconds, snapshot.GameMode);
    }

    /// <summary>读取游戏客户端返回的当前玩法模式。</summary>
    public Task<string?> GetGameModeAsync(CancellationToken cancellationToken = default) =>
        LocalLiveClientDataReader.GetGameModeAsync(cancellationToken);

    /// <summary>读取当前对局已进行的秒数。</summary>
    public Task<int?> GetGameTimeSecondsAsync(CancellationToken cancellationToken = default) =>
        LocalLiveClientDataReader.GetGameTimeSecondsAsync(cancellationToken);
}
