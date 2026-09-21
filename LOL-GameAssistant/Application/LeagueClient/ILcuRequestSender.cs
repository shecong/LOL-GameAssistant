namespace LOL_GameAssistant.Application.LeagueClient;

/// <summary>
/// 本机 League Client 请求端口。
/// 应用用例只表达“发送什么请求”，不绑定 HttpClient、认证 token 或具体 URL。
/// </summary>
public interface ILcuRequestSender
{
    /// <summary>读取 LCU JSON 响应；连接或响应失败时返回 null。</summary>
    Task<string?> GetStringAsync(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>读取 LCU 二进制资源，例如头像；失败时返回 null。</summary>
    Task<byte[]?> GetBytesAsync(string endpoint, CancellationToken cancellationToken = default);

    Task<bool> PostAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default);
}
