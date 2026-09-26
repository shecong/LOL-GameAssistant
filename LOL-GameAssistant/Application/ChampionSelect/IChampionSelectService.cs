using LOL_GameAssistant.Domain.ChampionSelect;

namespace LOL_GameAssistant.Application.ChampionSelect;

/// <summary>选人会话读取与自动禁用、选用用例端口。</summary>
public interface IChampionSelectService
{
    Task<ChampionSelectionSnapshot?> GetSessionAsync(CancellationToken cancellationToken = default);

    Task<bool> AutoBanAsync(IReadOnlyList<int> championIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按优先级选择可用英雄。<paramref name="lockIn"/> 为 false 时仅预选，不会锁定。
    /// </summary>
    Task<bool> AutoPickAsync(
        IReadOnlyList<int> championIds,
        bool lockIn,
        CancellationToken cancellationToken = default);
}