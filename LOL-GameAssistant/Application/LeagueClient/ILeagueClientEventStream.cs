using LOL_GameAssistant.Domain.LeagueClient;

namespace LOL_GameAssistant.Application.LeagueClient;

/// <summary>
/// 本地 LCU 游戏流程事件流端口。
/// 应用层只接收已解析事件，WebSocket 地址、认证和协议订阅由基础设施隐藏。
/// </summary>
public interface ILeagueClientEventStream : IDisposable, IAsyncDisposable
{
    event Action<LeagueClientEvent>? EventReceived;

    event Action<string>? ErrorOccurred;

    event Action<bool>? ConnectionChanged;

    event Action<string>? Reconnecting;

    /// <summary>连接当前 LCU；客户端未启动或认证不可用时返回 false。</summary>
    Task<bool> ConnectAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);

    /// <summary>订阅 LCU JSON API 事件。</summary>
    Task SubscribeToJsonApiEventsAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync();
}