using LOL_GameAssistant.Application.LeagueClient;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

public sealed class LcuClientCapabilityService(ILcuRequestSender lcu) : IClientCapabilityService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ClientCapabilitySnapshot? _snapshot;
    private long _generation;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string Method, string Path), ClientCapability> _observations = new();
    public void Invalidate() { Interlocked.Increment(ref _generation); _snapshot = null; _observations.Clear(); }
    public void Observe(string method, string path, System.Net.HttpStatusCode? status, LcuFailure failure)
    {
        if (path.StartsWith("/swagger/", StringComparison.Ordinal)) return;
        var state = failure == LcuFailure.NoCredentials || status == System.Net.HttpStatusCode.Unauthorized ? ClientCapabilityState.AuthenticationRequired :
            status == System.Net.HttpStatusCode.Forbidden ? ClientCapabilityState.Forbidden : status == System.Net.HttpStatusCode.NotFound ? ClientCapabilityState.ContextUnavailable :
            failure == LcuFailure.None && status is >= System.Net.HttpStatusCode.OK and < System.Net.HttpStatusCode.MultipleChoices ? ClientCapabilityState.Observed : ClientCapabilityState.Unknown;
        string reason = state switch { ClientCapabilityState.AuthenticationRequired => "未登录或认证已变化", ClientCapabilityState.Forbidden => "当前账号或阶段没有操作权限", ClientCapabilityState.ContextUnavailable => "资源当前不存在，可能是阶段条件未满足；不判定永久不支持", ClientCapabilityState.Observed => "最近一次请求被客户端接受，仍不代表后台任务已完成", _ => $"最近一次请求：{failure}，HTTP {status?.ToString() ?? "未取得"}" };
        string route = path.Split('?')[0];
        if (route.StartsWith("/riotclient/v1/auth-tokens/", StringComparison.Ordinal)) route = "/riotclient/v1/auth-tokens/{redacted}";
        if (_observations.Count > 1024) _observations.Clear();
        _observations[(method.ToUpperInvariant(), route)] = new(method.ToUpperInvariant(), route, state, reason);
    }
    private ClientCapabilitySnapshot Merge(ClientCapabilitySnapshot value) => value with
    { Capabilities = value.Capabilities.Where(c => !_observations.ContainsKey((c.Method, c.Path))).Concat(_observations.Values).ToArray() };
    public async Task<ClientCapabilitySnapshot> RefreshAsync(CancellationToken token = default, bool force = false)
    {
        if (!force && _snapshot is { } cached && DateTimeOffset.UtcNow - cached.CheckedAt < TimeSpan.FromMinutes(5)) return Merge(cached);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!force && _snapshot is { } ready && DateTimeOffset.UtcNow - ready.CheckedAt < TimeSpan.FromMinutes(5)) return Merge(ready);
            long generation = Volatile.Read(ref _generation);
            var entries = new List<ClientCapability>();
            JObject? schema = null;
            foreach (string endpoint in new[] { "/swagger/v3/openapi.json", "/swagger/v2/swagger.json" })
            {
                try { var json = await lcu.GetStringAsync(endpoint, token).ConfigureAwait(false); if (!string.IsNullOrWhiteSpace(json)) schema = JObject.Parse(json); }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { /* 模块目录未公开时保留未知，继续已有正常功能。 */ }
                if (schema?["paths"] is JObject) break;
            }
            if (schema?["paths"] is JObject paths)
                foreach (var route in paths.Properties())
                    if (route.Value is JObject methods)
                        foreach (var method in methods.Properties().Where(m => m.Name is "get" or "post" or "put" or "patch" or "delete" or "head"))
                            entries.Add(new(method.Name.ToUpperInvariant(), route.Name, ClientCapabilityState.Advertised, "当前客户端接口目录已列出；仍需满足阶段与权限条件"));
            string region = "未知";
            try { var raw = await lcu.GetStringAsync("/riotclient/region-locale", token).ConfigureAwait(false); region = string.IsNullOrWhiteSpace(raw) ? "未知" : JObject.Parse(raw).Value<string>("region") ?? "未知"; }
            catch (OperationCanceledException) { throw; }
            catch { }
            var result = new ClientCapabilitySnapshot(DateTimeOffset.UtcNow, schema?.SelectToken("info.version")?.ToString() ?? "未提供", region,
                schema?["paths"] is JObject, entries, entries.Count > 0 ? $"已读取 {entries.Count} 项方法与路径；未列出的能力保持待核实" : "接口目录暂不可用；保留已有功能，不据此禁用或试写接口");
            if (generation == Volatile.Read(ref _generation)) _snapshot = result;
            return Merge(result);
        }
        finally { _gate.Release(); }
    }
    public async Task<ClientCapability> CheckAsync(string method, string path, CancellationToken token = default)
    {
        var snapshot = await RefreshAsync(token).ConfigureAwait(false);
        string route = path.Split('?')[0];
        if (_observations.TryGetValue((method.ToUpperInvariant(), route), out var observed)) return observed;
        var matches = snapshot.Capabilities.Where(x => Matches(x.Path, route)).ToArray();
        var declared = _snapshot?.Capabilities.Where(x => Matches(x.Path, route)).ToArray() ?? [];
        return matches.FirstOrDefault(x => x.Method.Equals(method, StringComparison.OrdinalIgnoreCase)) ??
            new(method, route, declared.Length == 0 ? ClientCapabilityState.Unknown : ClientCapabilityState.MethodUnavailable,
                declared.Length == 0 ? "当前目录未列出该路径，不能仅据此断定不支持" : $"目录列出的方法：{string.Join("、", declared.Select(x => x.Method))}");
    }
    internal static bool Matches(string template, string route) =>
        Regex.IsMatch(route, "^" + string.Join("[^/]+", Regex.Split(template, @"\{[^}]+\}").Select(Regex.Escape)) + "$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
}
