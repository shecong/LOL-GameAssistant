using LOL_GameAssistant.Application.LeagueClient;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>
/// ILcuRequestSender 的本机 LCU 实现。
/// HttpClientHelper 继续统一处理认证、证书与连接生命周期；其它层不读取 LCU token。
/// </summary>
public sealed class LcuHttpRequestSender : ILcuRequestSender
{
    /// <summary>读取接口响应文本，并关闭已消费的响应流。</summary>
    public async Task<string?> GetStringAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClientHelper();
        using Stream? response = await client.GetAsync(endpoint, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (response == null) return null;
        using var reader = new StreamReader(response);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>读取接口响应的二进制内容，并关闭原始响应流。</summary>
    public async Task<byte[]?> GetBytesAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClientHelper();
        using Stream? response = await client.GetAsync(endpoint, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (response == null) return null;
        using var memory = new MemoryStream();
        await response.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        return memory.ToArray();
    }

    /// <summary>向 LCU 提交 JSON 数据，以是否取得响应流判断操作结果。</summary>
    public async Task<bool> PostAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClientHelper();
        using Stream? response = await client.PostAsync(
            endpoint,
            body: jsonBody,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return response != null;
    }

    /// <summary>向 LCU 更新 JSON 资源，以是否取得响应流判断操作结果。</summary>
    public async Task<bool> PutAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClientHelper();
        using Stream? response = await client.PutAsync(
            endpoint,
            body: jsonBody,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return response != null;
    }

    /// <summary>向 LCU 提交部分更新，以是否取得响应流判断操作结果。</summary>
    public async Task<bool> PatchAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClientHelper();
        using Stream? response = await client.PatchAsync(
            endpoint,
            body: jsonBody,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return response != null;
    }

    /// <summary>请求删除 LCU 资源，以是否取得响应流判断操作结果。</summary>
    public async Task<bool> DeleteAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClientHelper();
        using Stream? response = await client.DeleteAsync(endpoint, cancellationToken: cancellationToken).ConfigureAwait(false);
        return response != null;
    }
}