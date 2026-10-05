using LOL_GameAssistant.Domain.LeagueClient;

namespace LOL_GameAssistant.Domain.Coaching;

/// <summary>Prefer the LCU gameflow session over the optional Live Client API for mode identity.</summary>
public static class LiveModeContextResolver
{
    /// <summary>结合 LCU 会话和 Live Client 模式生成模式键、队列及展示名称。</summary>
    public static (string GameMode, int QueueId, string DisplayMode) Resolve(
        ActiveGameSnapshot? session, string? liveClientMode)
    {
        string gameMode = !string.IsNullOrWhiteSpace(session?.GameMode)
            ? session.GameMode : liveClientMode ?? "CLASSIC";
        int queueId = session?.QueueId ?? 0;
        string display = queueId is 2400 or 3270 ||
            gameMode.StartsWith("KIWI", StringComparison.OrdinalIgnoreCase)
                ? "海克斯大乱斗"
                : gameMode.Trim().ToUpperInvariant() switch
                {
                    "ARAM" => "深渊大乱斗",
                    "CHERRY" or "ARENA" => "斗魂竞技场",
                    "URF" or "ARURF" => "无限火力",
                    "NEXUSBLITZ" or "NEXUS_BLITZ" => "极限闪击",
                    _ => "峡谷对局"
                };
        return (gameMode, queueId, display);
    }
}
