using LOL_GameAssistant.Application.LeagueClient;
using System.Text;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>保留状态分类与空正文；兼容入口仍保留 null/bool 约定。</summary>
public sealed class LcuHttpRequestSender : ILcuResultRequestSender
{
    public event Action<string, string, System.Net.HttpStatusCode?, LcuFailure>? RequestCompleted;
    private readonly Func<string, string, string?, CancellationToken, Task<LcuHttpResult>> _send;
    public LcuHttpRequestSender() : this(async (method, endpoint, body, token) =>
    {
        using var client = new HttpClientHelper();
        return await client.SendRequestResultAsync(method, endpoint, body: body, cancellationToken: token).ConfigureAwait(false);
    }) { }
    internal LcuHttpRequestSender(Func<string, string, string?, CancellationToken, Task<LcuHttpResult>> send) => _send = send;
    public async Task<LcuResponse<byte[]>> SendAsync(string method, string endpoint, string? body = null, CancellationToken cancellationToken = default)
    {
        var response = await SendCoreAsync(method, endpoint, body, cancellationToken).ConfigureAwait(false);
        RequestCompleted?.Invoke(method, endpoint, response.StatusCode, response.Failure); return response;
    }
    private async Task<LcuResponse<byte[]>> SendCoreAsync(string method, string endpoint, string? body, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await _send(method, endpoint, body, cancellationToken).ConfigureAwait(false);
        using Stream? stream = result.Content;
        LcuFailure failure = result.Failure switch
        {
            LcuHttpFailure.None => LcuFailure.None,
            LcuHttpFailure.NoCredentials => LcuFailure.NoCredentials,
            LcuHttpFailure.HttpStatus => LcuFailure.HttpStatus,
            LcuHttpFailure.Timeout => LcuFailure.Timeout,
            LcuHttpFailure.Network => LcuFailure.Network,
            _ => LcuFailure.Unexpected
        };
        if (failure == LcuFailure.None && result.StatusCode is not (>= System.Net.HttpStatusCode.OK and < System.Net.HttpStatusCode.MultipleChoices))
            failure = result.StatusCode == null ? LcuFailure.Unexpected : LcuFailure.HttpStatus;
        if (failure != LcuFailure.None) return new(null, result.StatusCode, failure) { ServerErrorCode = result.ServerErrorCode };
        using var buffer = new MemoryStream();
        if (stream != null) await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return new(buffer.ToArray(), result.StatusCode, failure);
    }
    public async Task<string?> GetStringAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var result = await SendAsync("GET", endpoint, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? Encoding.UTF8.GetString(result.Data ?? []) : null;
    }
    public async Task<byte[]?> GetBytesAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var result = await SendAsync("GET", endpoint, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? result.Data : null;
    }
    public async Task<bool> PostAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default) =>
        (await SendAsync("POST", endpoint, jsonBody, cancellationToken).ConfigureAwait(false)).Succeeded;
    public async Task<bool> PutAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default) =>
        (await SendAsync("PUT", endpoint, jsonBody, cancellationToken).ConfigureAwait(false)).Succeeded;
    public async Task<bool> PatchAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default) =>
        (await SendAsync("PATCH", endpoint, jsonBody, cancellationToken).ConfigureAwait(false)).Succeeded;
    public async Task<bool> DeleteAsync(string endpoint, CancellationToken cancellationToken = default) =>
        (await SendAsync("DELETE", endpoint, cancellationToken: cancellationToken).ConfigureAwait(false)).Succeeded;
}
