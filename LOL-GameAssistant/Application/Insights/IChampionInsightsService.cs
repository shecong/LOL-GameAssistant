namespace LOL_GameAssistant.Application.Insights;

/// <summary>读取公开的 OP.GG 英雄层级与 ARAM 平衡修正数据，不会写入客户端。</summary>
public interface IChampionInsightsService
{
    Task<IReadOnlyList<ChampionTierInsight>> GetChampionTiersAsync(
        string mode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChampionBalanceAdjustment>> GetAramBalanceAdjustmentsAsync(
        CancellationToken cancellationToken = default);
}

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