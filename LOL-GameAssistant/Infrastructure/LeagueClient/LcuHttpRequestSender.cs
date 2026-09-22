using LOL_GameAssistant.Application.LeagueClient;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>
/// ILcuRequestSender 的本机 LCU 实现。
/// HttpClentHelper 继续统一处理认证、证书与连接生命周期；其它层不读取 LCU token。
/// </summary>
public sealed class LcuHttpRequestSender : ILcuRequestSender
{
    public async Task<string?> GetStringAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClentHelper();
        using Stream? response = await client.GetAsync(endpoint, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (response == null) return null;
        using var reader = new StreamReader(response);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]?> GetBytesAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClentHelper();
        using Stream? response = await client.GetAsync(endpoint, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (response == null) return null;
        using var memory = new MemoryStream();
        await response.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        return memory.ToArray();
    }

    public async Task<bool> PostAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClentHelper();
        using Stream? response = await client.PostAsync(
            endpoint,
            body: jsonBody,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return response != null;
    }

    public async Task<bool> PutAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClentHelper();
        using Stream? response = await client.PutAsync(
            endpoint,
            body: jsonBody,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return response != null;
    }

    public async Task<bool> DeleteAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClentHelper();
        using Stream? response = await client.DeleteAsync(endpoint, cancellationToken: cancellationToken).ConfigureAwait(false);
        return response != null;
    }
}