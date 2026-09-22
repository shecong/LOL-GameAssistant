using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Domain.Coaching;

namespace LOL_GameAssistant.Infrastructure.Coaching;

/// <summary>
/// 本地时间线规则。规则仅消费玩家正常可见的游戏时间、金币、装备和阵容信息，
/// 因而没有云端密钥时也能稳定提供有触发依据的建议。
/// </summary>
public sealed class TimelineRecommendationService : ILocalRecommendationService
{
    public IReadOnlyList<CoachRecommendation> Create(AiGameContext context, DateTimeOffset now)
    {
        if (string.Equals(context.Phase, "ChampSelect", StringComparison.OrdinalIgnoreCase))
            return CreateChampSelectRecommendations(context, now);
        if (!string.Equals(context.Phase, "InProgress", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<CoachRecommendation>();

        var recommendations = new List<CoachRecommendation>();
        int minute = Math.Max(0, context.GameTimeSeconds / 60);
        string stage = minute switch
        {
            < 8 => "early",
            < 14 => "lane",
            < 22 => "mid",
            _ => "late"
        };

        recommendations.Add(Create(
            $"timeline-{stage}-{context.Mode}",
            "时间线",
            minute >= 22 ? RecommendationPriority.Important : RecommendationPriority.Attention,
            minute switch
            {
                < 8 => "对线前期：先保证经验与补刀",
                < 14 => "转线窗口：先处理兵线再支援",
                < 22 => "中期：为下一处公开地图目标做准备",
                _ => "后期：优先与队友同步，保留保命空间"
            },
            minute switch
            {
                < 8 => "在可见敌方英雄和关键技能窗口明确前，优先稳定兵线与补刀，避免为无信息换血冒险。",
                < 14 => "清线后再选择河道资源、边线支援或回城；不要带着未处理的兵线盲目离线。",
                < 22 => "先让兵线和视野准备与下一处公开目标的时间对齐，没有队友跟进时不要独自深入野区。",
                _ => "团战前优先确认队友位置与自己的回撤路径；大额金币优先转化为成装或防御位。"
            },
            $"当前时间 {context.GameTimeText}，处于{GetStageName(stage)}。",
            now));

        int goldTier = context.CurrentGold switch
        {
            >= 1800 => 3,
            >= 900 => 2,
            >= 350 => 1,
            _ => 0
        };
        if (goldTier > 0)
        {
            recommendations.Add(Create(
                $"gold-{goldTier}-{context.MyChampionId}",
                "经济",
                goldTier >= 2 ? RecommendationPriority.Attention : RecommendationPriority.Info,
                goldTier switch
                {
                    3 => "经济充足：规划下一次安全回城",
                    2 => "已有关键组件经济",
                    _ => "可利用安全窗口补给"
                },
                goldTier switch
                {
                    3 => "下一次安全回城可优先完成核心组件或成装，避免长时间带着大量金币参与高风险交战。",
                    2 => "有安全回城窗口时可补一到两个关键组件，再回到兵线或公开地图目标附近。",
                    _ => "可根据当前局势补基础组件或消耗品；没有安全窗口时先保持兵线和经验。"
                },
                $"当前可见金币：{context.CurrentGold}。",
                now));
        }

        if (context.CurrentItems.Count == 0)
        {
            recommendations.Add(Create(
                $"items-empty-{minute / 3}",
                "装备",
                RecommendationPriority.Info,
                "装备状态尚未读取完整",
                "建议在下一个安全窗口确认已购装备与核心路线；若本机 Live Client 数据暂未就绪，稍后会自动重试。",
                "未读取到已购装备。",
                now));
        }
        else
        {
            string items = string.Join("、", context.CurrentItems.Take(4));
            recommendations.Add(Create(
                $"items-{GetStableHash(context.CurrentItems)}",
                "装备",
                RecommendationPriority.Info,
                "根据已购装备规划下一件组件",
                "继续沿当前核心路线补足关键组件；遭遇明显的可见伤害威胁时，可考虑将一个装备位调整为对应的防御或保命选择。",
                "已购装备：" + items,
                now));
        }

        recommendations.Add(Create(
            $"lane-{context.MyChampionId}-{Normalize(context.MyRole)}-{minute / 5}",
            "对线知识",
            RecommendationPriority.Info,
            $"{context.MyChampion} · {NormalizeRole(context.MyRole)} 对线提示",
            context.LaneKnowledge,
            context.EnemyChampions.Count > 0
                ? "已知敌方阵容：" + string.Join("、", context.EnemyChampions.Take(5))
                : "敌方阵容仍在同步。",
            now));

        return recommendations;
    }

    private static IReadOnlyList<CoachRecommendation> CreateChampSelectRecommendations(AiGameContext context, DateTimeOffset now)
    {
        var recommendations = new List<CoachRecommendation>();
        if (context.MyChampionId > 0)
        {
            recommendations.Add(Create(
                $"select-{context.MyChampionId}-{Normalize(context.MyRole)}",
                "选人",
                RecommendationPriority.Attention,
                $"{context.MyChampion} · {NormalizeRole(context.MyRole)} 选人校验",
                "确认符文、召唤师技能和对线计划与当前分路一致；锁定后可在本页使用 OP.GG 一键配置选择不同出装路线。",
                context.EnemyChampions.Count > 0
                    ? "已知敌方选择：" + string.Join("、", context.EnemyChampions)
                    : "敌方英雄仍在选择中。",
                now));
        }
        else
        {
            recommendations.Add(Create(
                "select-awaiting-champion",
                "选人",
                RecommendationPriority.Info,
                "等待锁定英雄",
                "锁定英雄后会结合分路、可见敌方选择和本地对线知识生成选人建议。",
                "当前尚未读取到已锁定英雄。",
                now));
        }

        return recommendations;
    }

    private static CoachRecommendation Create(
        string id,
        string category,
        RecommendationPriority priority,
        string title,
        string body,
        string evidence,
        DateTimeOffset now) =>
        new(id, category, priority, title, body, evidence, RecommendationSource.LocalRules, now.AddMinutes(3));

    private static string GetStageName(string stage) => stage switch
    {
        "early" => "对线前期",
        "lane" => "对线转线期",
        "mid" => "中期",
        _ => "后期"
    };

    private static string NormalizeRole(string role) => string.IsNullOrWhiteSpace(role) || role == "通用" ? "通用位置" : role;

    private static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? "general" : value.Trim().ToLowerInvariant();

    private static string GetStableHash(IEnumerable<string> values)
    {
        string text = string.Join("|", values.OrderBy(value => value, StringComparer.Ordinal));
        return unchecked((uint)StringComparer.Ordinal.GetHashCode(text)).ToString("X");
    }
}