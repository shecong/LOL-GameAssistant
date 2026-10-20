using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.Application.Teams;

namespace LOL_GameAssistant.Application.Matches;

public sealed record PlayerQueryRegion(string Platform, string Name)
{
    public override string ToString() => Name;
}

public sealed record PlayerQueryServices(IPlayerProfileService Profiles, IMatchHistoryService History,
    IRankedStatsService Ranked, IPremadeDetectionService Premades);

/// <summary>按查询窗口选择数据源，不修改客户端登录大区或全局服务。</summary>
public interface IRegionalPlayerQueryFactory
{
    IReadOnlyList<PlayerQueryRegion> Regions { get; }
    PlayerQueryServices Create(string? platform);
}
