using System.Security.Cryptography;
using System.Text;

namespace LOL_GameAssistant.Domain.Coaching;

/// <summary>以决策区间和事件区分变化；具体数值继续采样，但不会每两秒重启云端请求。</summary>
public static class RecommendationPolicy
{
    public static string Identity(AiGameContext c) => $"{c.Phase}|{c.GameMode}|{c.QueueId}|{c.MyChampionId}|{c.MyRole}";
    public static string Version(AiGameContext c) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("|", Identity(c), c.MatchupChampion, c.Phase == "InProgress" && c.GameTimeSeconds >= 840 ? "中后期" : "前期",
            Join(c.CurrentItems), Join(c.AlliedChampions), Join(c.EnemyChampions), Join(c.ConfirmedAugmentOptions),
            c.Telemetry == null ? "" : string.Join(";", CoachingDecisionFrame.Create(c).Facts
                .Where(f => f.Id is not ("time" or "self.cs" or "gold") && !f.Id.EndsWith(".score", StringComparison.Ordinal) &&
                    (!f.Id.EndsWith(".level", StringComparison.Ordinal) || f.Id == "self.level"))
                .Select(f => f.Id + "=" + (f.Id == "self.level" ? (c.Telemetry.Level < 6 ? "pre6" : c.Telemetry.Level < 11 ? "6" : c.Telemetry.Level < 16 ? "11" : "16") :
                    f.Id == "self.abilities" ? string.Join(",", c.Telemetry.Abilities.Select(a => a.Slot + ":" + (a.Slot == "R" ? a.Rank : a.Rank > 0 ? 1 : 0))) : f.Version))),
            c.Telemetry == null ? "" : string.Join(",", CoachingDecisionFrame.Create(c).Opportunities.Select(o => o.Id))))));
    private static string Join(IReadOnlyList<string> values) => string.Join(",", values.OrderBy(v => v, StringComparer.Ordinal));
    public static string Trigger(AiGameContext? previous, AiGameContext next)
    {
        if (previous == null || Identity(previous) != Identity(next)) return "英雄、模式或阶段变化";
        if (Join(previous.CurrentItems) != Join(next.CurrentItems)) return "已购装备变化";
        if (Join(previous.ConfirmedAugmentOptions) != Join(next.ConfirmedAugmentOptions)) return "已确认增幅候选变化";
        if (previous.MatchupChampion != next.MatchupChampion) return "对线对象更新";
        if (previous.Telemetry?.Self?.IsDead != next.Telemetry?.Self?.IsDead) return "阵亡或复活";
        if (previous.Telemetry?.HealthPercent is { } before && next.Telemetry?.HealthPercent is { } after &&
            CoachingDecisionFrame.HealthBand(before) != CoachingDecisionFrame.HealthBand(after)) return "生命状态变化";
        if (previous.Telemetry?.Events.LastOrDefault()?.Id != next.Telemetry?.Events.LastOrDefault()?.Id) return "近期战斗或资源事件";
        return "阵容、等级或决策窗口变化";
    }
    public static bool IsValid(CoachRecommendation card, AiGameContext context, DateTimeOffset now) =>
        card.ExpiresAt > now && (card.OpportunityId.Length == 0 ? card.ContextVersion == Version(context) :
            CoachingDecisionFrame.Create(context).Opportunities.Any(o => o.Id == card.OpportunityId) &&
            card.ContextVersion == EvidenceVersion(context, card.EvidenceFields));

    public static string EvidenceVersion(AiGameContext c, IReadOnlyList<string> fields)
    {
        var facts = CoachingDecisionFrame.Create(c).Facts.ToDictionary(f => f.Id, StringComparer.Ordinal);
        string value = Identity(c) + "|" + string.Join("|", fields.OrderBy(x => x, StringComparer.Ordinal)
            .Select(id => id + "=" + (facts.TryGetValue(id, out var fact) ? fact.Version : "missing")));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    public static bool HasField(AiGameContext c, string field) => field switch
    {
        "champion" => c.MyChampionId > 0,
        "role" => c.MyRole is not ("通用" or "未知位置" or ""),
        "allies" => c.AlliedChampions.Count > 0,
        "enemies" => c.EnemyChampions.Count > 0,
        "items" or "gold" or "time" => c.HasLiveClientData,
        "matchup" => !string.IsNullOrWhiteSpace(c.MatchupChampion),
        "augments" => c.ConfirmedAugmentOptions.Count > 0,
        _ => false
    };
}
