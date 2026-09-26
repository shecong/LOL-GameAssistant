using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.LoLApi;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>
/// 适配现有 lockfile/WMI 探测实现，将端口和 token 的全局设置收敛在基础设施层。
/// 其它层只关心“是否已连接”，不直接访问认证信息。
/// </summary>
public sealed class LeagueClientConnection : ILeagueClientConnection
{
    public bool TryConnect() => TryGetCredentials() != null;

    /// <summary>仅供基础设施内部的 LCU HTTP/WebSocket 适配器读取认证信息。</summary>
    public LeagueClientCredentials? TryGetCredentials(bool forceRefresh = false)
    {
        if (!forceRefresh &&
            !string.IsNullOrWhiteSpace(HttpClientHelper.Port) &&
            !string.IsNullOrWhiteSpace(HttpClientHelper.Token))
        {
            return new LeagueClientCredentials(HttpClientHelper.Port, HttpClientHelper.Token);
        }

        (string? port, string? token) = GetlolLcu.GetAuth();
        if (string.IsNullOrWhiteSpace(port) || string.IsNullOrWhiteSpace(token)) return null;
        HttpClientHelper.Port = port;
        HttpClientHelper.Token = token;
        return new LeagueClientCredentials(port, token);
    }
}