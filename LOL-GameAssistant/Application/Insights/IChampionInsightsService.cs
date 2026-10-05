namespace LOL_GameAssistant.Application.Insights;

/// <summary>读取公开的 OP.GG 英雄层级与 ARAM 平衡修正数据，不会写入客户端。</summary>
public interface IChampionInsightsService
{
    /// <summary>查询指定模式下的英雄梯度信息。</summary>
    Task<IReadOnlyList<ChampionTierInsight>> GetChampionTiersAsync(
        string mode,
        CancellationToken cancellationToken = default);

    /// <summary>查询英雄在大乱斗模式中的平衡调整。</summary>
    Task<IReadOnlyList<ChampionBalanceAdjustment>> GetAramBalanceAdjustmentsAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>外部英雄梯度数据及其显示信息。</summary>
public sealed record ChampionTierInsight(
    int ChampionId,
    string ChampionName,
    string Mode,
    string Tier,
    int Rank,
    double WinRate,
    double PickRate);

/// <summary>ARAM/特殊模式公开平衡值；例如 105 代表伤害 +5%。</summary>
public sealed record ChampionBalanceAdjustment(
    int ChampionId,
    string ChampionName,
    double DamageDealt,
    double DamageTaken,
    double Healing,
    double ShieldAmount,
    double Tenacity,
    double CooldownReduction);