using LOL_GameAssistant.Application.LiveGame;
using LOL_GameAssistant.Domain.LiveGame;

namespace LOL_GameAssistant.Infrastructure.LiveGame;

/// <summary>本机 Live Client Data API 的基础设施适配器。</summary>
public sealed class LiveClientGameStateService : ILiveClientGameStateService
{
    public async Task<LivePlayerState?> GetOwnStateAsync(CancellationToken cancellationToken = default)
    {
        LocalLiveClientOwnState? state = await LocalLiveClientDataReader.GetOwnStateAsync(cancellationToken).ConfigureAwait(false);
        return state == null ? null : new LivePlayerState(state.CurrentGold, state.Items, state.GameTimeSeconds);
    }

    public async Task<LiveClientGameSnapshot?> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        LocalLiveClientSnapshot? snapshot = await LocalLiveClientDataReader.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return snapshot == null
            ? null
            : new LiveClientGameSnapshot(snapshot.CurrentGold, snapshot.Items, snapshot.GameTimeSeconds, snapshot.GameMode);
    }

    public Task<string?> GetGameModeAsync(CancellationToken cancellationToken = default) =>
        LocalLiveClientDataReader.GetGameModeAsync(cancellationToken);

    public Task<int?> GetGameTimeSecondsAsync(CancellationToken cancellationToken = default) =>
        LocalLiveClientDataReader.GetGameTimeSecondsAsync(cancellationToken);
}