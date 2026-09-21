namespace LOL_GameAssistant.Domain.LeagueClient;

/// <summary>LCU WebSocket 与本地请求共享的短期认证信息。</summary>
public sealed record LeagueClientCredentials(string Port, string Token);
