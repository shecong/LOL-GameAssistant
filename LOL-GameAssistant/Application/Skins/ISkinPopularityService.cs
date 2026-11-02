namespace LOL_GameAssistant.Application.Skins;

/// <summary>OP.GG 样本内的皮肤使用场数与该英雄选用率（百分数），不代表国服全量数据或好评率。</summary>
public sealed record SkinUsageStatistics(int SkinNumber, long Plays, double PickRate);

public interface ISkinPopularityService
{
    Task<IReadOnlyList<SkinUsageStatistics>> GetStatisticsAsync(string model, CancellationToken cancellationToken = default);
}
