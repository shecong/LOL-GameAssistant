using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.LoLApi;

namespace LOL_GameAssistant.Infrastructure.GameData;

/// <summary>将 Data Dragon 版本缓存初始化从界面层收口到基础设施层。</summary>
public sealed class LegacyGameDataVersionService : IGameDataVersionService
{
    public Task EnsureCurrentVersionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Game_Api.GetGameversion();
    }
}