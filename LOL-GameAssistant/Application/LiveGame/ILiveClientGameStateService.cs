using LOL_GameAssistant.Domain.LiveGame;

namespace LOL_GameAssistant.Application.LiveGame;

/// <summary>读取本机 Live Client Data API 中允许展示的当前玩家状态。</summary>
public interface ILiveClientGameStateService
{
    Task<LivePlayerState?> GetOwnStateAsync(CancellationToken cancellationToken = default);

    Task<string?> GetGameModeAsync(CancellationToken cancellationToken = default);

    Task<int?> GetGameTimeSecondsAsync(CancellationToken cancellationToken = default);
}
