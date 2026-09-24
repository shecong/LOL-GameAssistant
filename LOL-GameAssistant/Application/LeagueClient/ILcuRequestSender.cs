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

    /// <summary>向 LCU 覆盖写入一个 JSON 资源。</summary>
    Task<bool> PutAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default);

    /// <summary>部分 LCU 资源（例如英雄选择动作）只允许局部更新。</summary>
    Task<bool> PatchAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default);

    /// <summary>删除一个 LCU 资源。调用方仅可删除自己创建、且明确标识过的资源。</summary>
    Task<bool> DeleteAsync(string endpoint, CancellationToken cancellationToken = default);
}
