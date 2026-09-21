using LOL_GameAssistant.Domain.Players;

namespace LOL_GameAssistant.Application.Players;

/// <summary>查询当前或指定召唤师资料的应用服务。</summary>
public interface IPlayerProfileService
{
    Task<PlayerProfile?> GetCurrentAsync(CancellationToken cancellationToken = default);
    Task<PlayerProfile?> GetByPuuidAsync(string puuid, CancellationToken cancellationToken = default);
    Task<PlayerProfile?> FindByRiotIdAsync(string gameName, string tagLine, CancellationToken cancellationToken = default);
}
