namespace LOL_GameAssistant.Domain.LeagueClient;

/// <summary>LCU 推送给应用的已解析事件，不携带认证信息或原始 JSON。</summary>
public sealed record LeagueClientEvent(string Uri, string Data);
