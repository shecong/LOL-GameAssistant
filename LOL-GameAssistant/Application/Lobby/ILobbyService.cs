using LOL_GameAssistant.Domain.LeagueClient;

namespace LOL_GameAssistant.Application.Lobby;

/// <summary>大厅、匹配确认与游戏流程的应用端口。</summary>
public interface ILobbyService
{
    /// <summary>向客户端提交开始匹配的请求。</summary>
    Task StartMatchmakingAsync(CancellationToken cancellationToken = default);

    /// <summary>向客户端提交接受匹配确认的请求。</summary>
    Task AcceptReadyCheckAsync(CancellationToken cancellationToken = default);

    /// <summary>读取当前大厅及成员信息。</summary>
    Task<LobbySnapshot?> GetLobbyAsync(CancellationToken cancellationToken = default);

    /// <summary>读取客户端当前游戏流程阶段。</summary>
    Task<string?> GetGameFlowPhaseAsync(CancellationToken cancellationToken = default);

    /// <summary>读取当前游戏会话的队伍和英雄信息。</summary>
    Task<ActiveGameSnapshot?> GetCurrentSessionAsync(CancellationToken cancellationToken = default);
}