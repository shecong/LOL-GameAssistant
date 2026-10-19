namespace LOL_GameAssistant.Application.LeagueClient;

public enum ClientCapabilityState { Advertised, Unknown, MethodUnavailable, Observed, AuthenticationRequired, Forbidden, ContextUnavailable }
public sealed record ClientCapability(string Method, string Path, ClientCapabilityState State, string Reason);
public sealed record ClientCapabilitySnapshot(DateTimeOffset CheckedAt, string Build, string Region, bool HasSchema,
    IReadOnlyList<ClientCapability> Capabilities, string Message);

/// <summary>仅通过接口目录识别能力；未知不等于不支持，不调用写操作探测。</summary>
public interface IClientCapabilityService
{
    Task<ClientCapabilitySnapshot> RefreshAsync(CancellationToken token = default, bool force = false);
    Task<ClientCapability> CheckAsync(string method, string path, CancellationToken token = default);
    void Invalidate();
    void Observe(string method, string path, System.Net.HttpStatusCode? status, LcuFailure failure) { }
}
