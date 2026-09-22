using LOL_GameAssistant.Domain.LeagueClient;

namespace LOL_GameAssistant.Application.Lobby;

/// <summary>大厅、匹配确认与游戏流程的应用端口。</summary>
public interface ILobbyService
{
    Task StartMatchmakingAsync(CancellationToken cancellationToken = default);

    Task AcceptReadyCheckAsync(CancellationToken cancellationToken = default);

    Task<LobbySnapshot?> GetLobbyAsync(CancellationToken cancellationToken = default);

    Task<string?> GetGameFlowPhaseAsync(CancellationToken cancellationToken = default);

    Task<ActiveGameSnapshot?> GetCurrentSessionAsync(CancellationToken cancellationToken = default);
}