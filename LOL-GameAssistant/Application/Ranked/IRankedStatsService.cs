using LOL_GameAssistant.Domain.Ranked;

namespace LOL_GameAssistant.Application.Ranked;

/// <summary>读取召唤师单双排与灵活组排数据的应用端口。</summary>
public interface IRankedStatsService
{
    /// <summary>按玩家标识读取排位概况，支持当前账号查询方式。</summary>
    Task<RankedOverview?> GetAsync(
        string puuid,
        bool isCurrentUser = false,
        CancellationToken cancellationToken = default);
}