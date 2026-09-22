using LOL_GameAssistant.Domain.LeagueClient;

namespace LOL_GameAssistant.Application.LeagueClient;

/// <summary>启动本地 LOL 客户端的应用端口。</summary>
public interface IGameClientLauncher
{
    GameClientLaunchResult Start(string? configuredDirectory);

    /// <summary>Start the client and verify that its visible UX and local LCU are ready.</summary>
    Task<GameClientLaunchResult> StartAndVerifyAsync(
        string? configuredDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>将旧版保存的 exe 路径规范为设置页可保存的安装目录。</summary>
    string NormalizeConfiguredDirectory(string? configuredDirectory);
}