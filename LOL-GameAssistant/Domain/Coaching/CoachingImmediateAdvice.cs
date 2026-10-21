namespace LOL_GameAssistant.Domain.Coaching;

/// <summary>生命警戒不等待网络；只使用本人确实可见的状态，不替玩家判断未知的战场位置。</summary>
public static class CoachingImmediateAdvice
{
    public static CoachRecommendation? Create(AiGameContext context, DateTimeOffset now)
    {
        if (context.Phase != "InProgress" || !context.HasLiveClientData || context.Telemetry?.Self?.IsDead != false ||
            context.Telemetry.HealthPercent is not (>= 0 and <= 30)) return null;
        var frame = CoachingDecisionFrame.Create(context);
        var opportunity = frame.Opportunities.FirstOrDefault(o => o.Id == "survival.health");
        if (opportunity == null) return null;
        string[] fields = ["champion", "self.health", "self.life"];
        string version = RecommendationPolicy.EvidenceVersion(context, fields);
        bool canRecall = !context.IsAram && context.GameMode.Trim().ToUpperInvariant() is "CLASSIC" or "CLASSIC SR";
        string[] steps = !canRecall
            ? ["停止为补刀或消耗向前走，先退到仍能保护你的队友后侧。", "沿可见的安全路线拉开距离，保留脱离追击的技能；不要为了购物主动送死。"]
            : ["停止为了补刀向前换血，先沿可见安全路线退到队友或己方防御塔一侧。", "确认敌人无法立即打断且不会被包夹后，再决定回城补给；不要原地硬读回城。"];
        return new CoachRecommendation("local-health-" + version[..12] + "-" + now.UtcTicks, "生存", RecommendationPriority.Important,
            "低血量：先停止前压", string.Join("；", steps),
            string.Join("\n", frame.Facts.Where(f => fields.Contains(f.Id)).Select(f => $"[{f.Id}] {f.Value}")),
            RecommendationSource.LocalObservation, now.AddSeconds(25))
        {
            OpportunityId = opportunity.Id, ActionKey = "survival:disengage", EvidenceFields = fields, ContextVersion = version,
            Steps = steps, GeneratedAt = now, Conditions = "你仍存活，生命不超过30%；先确认退路可见且没有敌人包夹。",
            Why = "本机确认当前生命处于警戒区间，继续前压会减少承受下一次伤害后的撤退余地；未判断附近敌人位置。",
            ExpectedOutcome = "减少额外承伤，保留脱离战斗或获得补给的机会。",
            Fallback = "退路被封时先靠近可支援队友，不穿过无视野区域。",
            InvalidWhen = "生命恢复到30%以上、阵亡或退路变化时停止执行。"
        };
    }
}
