namespace LOL_GameAssistant.Domain.LeagueClient;

/// <summary>启动 LOL 客户端后的领域结果，不暴露进程或文件系统实现细节。</summary>
public sealed record GameClientLaunchResult(bool Started, string Message, string? ExecutablePath = null);
