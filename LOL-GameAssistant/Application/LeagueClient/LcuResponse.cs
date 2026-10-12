using System.Net;
using System.Text.Json;

namespace LOL_GameAssistant.Application.LeagueClient;

public enum LcuFailure { None, NoCredentials, HttpStatus, Timeout, Network, Unexpected, InvalidJson }

/// <summary>应用可读取的响应；空的成功正文与传输失败明确区分。</summary>
public sealed record LcuResponse<T>(T? Data, HttpStatusCode? StatusCode, LcuFailure Failure)
{
    public bool Succeeded => Failure == LcuFailure.None && StatusCode is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices;
}

public interface ILcuResultRequestSender : ILcuRequestSender
{
    Task<LcuResponse<byte[]>> SendAsync(string method, string endpoint, string? body = null, CancellationToken cancellationToken = default);
}

public sealed class LcuRequestException(LcuFailure failure, HttpStatusCode? statusCode)
    : HttpRequestException($"客户端请求失败：{failure}" + (statusCode == null ? "" : $" (HTTP {(int)statusCode})"), null, statusCode)
{
    public LcuFailure Failure { get; } = failure;
}

public static class LcuResponseExtensions
{
    public static async Task<LcuResponse<T>> GetJsonAsync<T>(this ILcuResultRequestSender sender,
        string endpoint, CancellationToken cancellationToken = default)
    {
        var response = await sender.SendAsync("GET", endpoint, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!response.Succeeded) return new(default, response.StatusCode, response.Failure);
        try
        {
            T? data = JsonSerializer.Deserialize<T>(response.Data ?? [], new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return new(data, response.StatusCode, data == null ? LcuFailure.InvalidJson : LcuFailure.None);
        }
        catch (JsonException) { return new(default, response.StatusCode, LcuFailure.InvalidJson); }
    }
}
