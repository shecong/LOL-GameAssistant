using LOL_GameAssistant.Domain.Players;

namespace LOL_GameAssistant.Application.Players;

/// <summary>查询当前或指定召唤师资料的应用服务。</summary>
public interface IPlayerProfileService
{
    /// <summary>读取当前登录召唤师的资料。</summary>
    Task<PlayerProfile?> GetCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>按玩家唯一标识读取召唤师资料。</summary>
    Task<PlayerProfile?> GetByPuuidAsync(string puuid, CancellationToken cancellationToken = default);

    /// <summary>按游戏名和标签查找召唤师资料。</summary>
    Task<PlayerProfile?> FindByRiotIdAsync(string gameName, string tagLine, CancellationToken cancellationToken = default);
}