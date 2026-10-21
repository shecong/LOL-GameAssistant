using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LOL_GameAssistant.Domain.Coaching;

namespace LOL_GameAssistant.Infrastructure.Ai;

/// <summary>生产路径的结构化契约。JSON 可读不代表建议有用，事实/场景/行动都必须匹配。</summary>
internal static class ActionableAdviceParser
{
    public static IReadOnlyList<CoachRecommendation> Parse(JsonElement root, AiGameContext context, DateTimeOffset now)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("recommendations", out var array) ||
            array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > 5) return Error(now);
        // 无需行动是正常分析结果，不强迫模型凑满三条。
        if (array.GetArrayLength() == 0) return [];
        var frame = CoachingDecisionFrame.Create(context);
        var facts = frame.Facts.ToDictionary(f => f.Id, StringComparer.Ordinal);
        var cards = new List<CoachRecommendation>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            string Text(string key) => item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
            string[] Strings(string key) => item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array &&
                v.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String)
                ? v.EnumerateArray().Select(x => x.GetString()!.Trim()).ToArray() : [];
            string opportunityId = Text("opportunityId"), action = Text("actionKey"), title = Text("title");
            string priorityText = Text("priority");
            string conditions = Text("conditions"), invalid = Text("invalidWhen"), why = Text("why"), outcome = Text("expectedOutcome"), fallback = Text("fallback");
            var opportunity = frame.Opportunities.FirstOrDefault(o => o.Id == opportunityId);
            string[] ids = Strings("factIds"), steps = Strings("steps");
            if (opportunity == null || priorityText is not ("info" or "attention" or "important") ||
                !opportunity.AllowedActions.Contains(action, StringComparer.Ordinal) ||
                ids.Length is < 2 or > 12 || ids.Distinct().Count() != ids.Length || ids.Any(id => !facts.ContainsKey(id)) ||
                opportunity.RequiredFacts.Any(id => !ids.Contains(id, StringComparer.Ordinal)) ||
                title.Length is < 4 or > 40 || steps.Length is < 2 or > 3 || steps.Any(s => s.Length is < 8 or > 150) ||
                steps.Distinct().Count() != steps.Length || conditions.Length is < 8 or > 160 || invalid.Length is < 6 or > 120 ||
                why.Length is < 12 or > 240 || outcome.Length is < 6 or > 120 || fallback.Length is < 8 or > 150) continue;
            string plan = string.Join("；", steps);
            if (opportunityId == "lane.matchup" && !(plan + why).Contains(context.MatchupChampion, StringComparison.Ordinal)) continue;
            if (opportunityId == "fight.plan" && !context.EnemyChampions.Concat(context.AlliedChampions)
                .Where(name => name != context.MyChampion && name.Length > 0).Any(name => (plan + why).Contains(name, StringComparison.Ordinal))) continue;
            // 套话可作为补充，不能独立充当执行步骤。要求至少一个明确动作和一个英雄/技能锚点。
            if (steps.Any(s => !HasConcreteAction(s)) ||
                !(new[] { context.MyChampion, context.MatchupChampion }.Concat(context.AlliedChampions).Concat(context.EnemyChampions)
                    .Where(n => !string.IsNullOrWhiteSpace(n)).Any(n => (plan + why).Contains(n, StringComparison.Ordinal)) ||
                  new[] { "Q", "W", "E", "R", "生命", "法力", "金币", "复活", "存活", "补刀", "候选" }.Any(n => (plan + why).Contains(n, StringComparison.Ordinal)))) continue;
            // 未验证的玩法不允许执行步骤或备选行动携带峡谷回城指令，包括拆开的购买措辞。
            if (!context.SupportsRiftAdvice && new[] { "回城", "传送回线", "传送回家" }
                .Any(term => (plan + fallback).Contains(term, StringComparison.Ordinal))) continue;
            RecommendationPriority requested = priorityText switch
            { "important" => RecommendationPriority.Important, "attention" => RecommendationPriority.Attention, _ => RecommendationPriority.Info };
            var priority = (RecommendationPriority)Math.Min((int)requested, (int)opportunity.MaxPriority);
            string version = RecommendationPolicy.EvidenceVersion(context, ids);
            string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(opportunityId + action + version + now.UtcTicks)))[..20];
            cards.Add(new CoachRecommendation("plan-" + id, opportunity.Category, priority, title, plan,
                string.Join("\n", ids.Select(key => $"[{key}] {facts[key].Value}")), RecommendationSource.CloudAi, now.AddSeconds(opportunity.LifetimeSeconds))
            {
                OpportunityId = opportunityId, ActionKey = action, EvidenceFields = ids, Steps = steps, Why = why,
                Conditions = conditions, InvalidWhen = invalid, ExpectedOutcome = outcome, Fallback = fallback,
                ContextVersion = version, GeneratedAt = now
            });
        }
        if (cards.Count == 0) return Error(now);
        return cards.OrderByDescending(c => c.Priority).GroupBy(c => c.OpportunityId).Select(g => g.First()).Take(3).ToArray();
    }

    private static bool HasConcreteAction(string step)
    {
        // 套话中的“走、站、补、看、打”不能单独满足动作检查；具体补充仍可保留。
        foreach (string phrase in new[] { "注意走位", "注意站位", "配合队友", "稳健发育", "提高容错",
            "保持警惕", "视情况出装", "看情况", "打好团战", "做好视野", "注意补刀" })
            step = step.Replace(phrase, "", StringComparison.Ordinal);
        return "退走拉等留交补推清回买站靠跟控绕打撤换看守检查确认".Any(step.Contains);
    }

    private static IReadOnlyList<CoachRecommendation> Error(DateTimeOffset now) =>
        [new("quality-error", "质量校验", RecommendationPriority.Info, "本次建议未通过质量校验", "缺少可核验依据、具体行动或适用条件。", "", RecommendationSource.CloudAi, now) { IsFormatError = true }];
}
