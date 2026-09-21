namespace LOL_GameAssistant.Application.GameData;

/// <summary>确保 Data Dragon 资源版本已准备好。</summary>
public interface IGameDataVersionService
{
    Task EnsureCurrentVersionAsync(CancellationToken cancellationToken = default);
}
