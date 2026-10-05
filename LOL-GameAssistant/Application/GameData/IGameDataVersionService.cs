namespace LOL_GameAssistant.Application.GameData;

/// <summary>确保 Data Dragon 资源版本已准备好。</summary>
public interface IGameDataVersionService
{
    /// <summary>初始化或刷新游戏资源使用的版本号。</summary>
    Task EnsureCurrentVersionAsync(CancellationToken cancellationToken = default);
}