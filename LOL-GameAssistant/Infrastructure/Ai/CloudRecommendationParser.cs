using LOL_GameAssistant.Domain.Coaching;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LOL_GameAssistant.Infrastructure.Ai;

/// <summary>兼容旧版 JSON 建议，并保证任何异常响应都不会把 JSON 原文直接展示给用户。</summary>
internal static class CloudRecommendationParser
{
    /// <summary>解析当前模块接收的外部数据。</summary>
    public static IReadOnlyList<CoachRecommendation> Parse(string content, DateTimeOffset now, AiGameContext? context = null, bool strict = false)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Length > 32000)
            return [CreateTextCard("建议内容为空或超过长度限制。", now) with { IsFormatError = true }];
        if (strict)
        {
            try
            {
                using var document = JsonDocument.Parse(RemoveMarkdownFence(content));
                if (context != null) return ActionableAdviceParser.Parse(document.RootElement, context, now);
            }
            catch (JsonException) { return [CreateTextCard("云端建议格式不完整，请稍后刷新重试。", now) with { IsFormatError = true }]; }
        }
        var cards = ParseContent(content, now);
        if (context == null) return cards;
        var valid = cards.Where(card => !card.IsFormatError && (!strict ||
                card.EvidenceFields.Count > 0 && !string.IsNullOrWhiteSpace(card.Conditions) && AllowedActions.Contains(card.ActionKey)) &&
            card.EvidenceFields.All(field => RecommendationPolicy.HasField(context, field)) &&
            card.ReferencedItems.All(item => context.CurrentItems.Contains(item, StringComparer.Ordinal)))
            .Select(card => card with { ContextVersion = RecommendationPolicy.Version(context), GeneratedAt = now,
                Evidence = card.EvidenceFields.Count == 0 ? "文本兼容输出，依据未经结构化核验。" : BuildEvidence(context, card.EvidenceFields) })
            .GroupBy(c => c.SemanticKey, StringComparer.Ordinal).Select(g => g.First()).Take(3).ToArray();
        return valid.Length > 0 ? valid : [CreateTextCard("建议格式异常或引用了缺失的数据，请重新分析。", now) with { IsFormatError = true }];
    }

    private static readonly HashSet<string> AllowedActions = new(StringComparer.Ordinal)
    { "build:damage", "build:survival", "build:utility", "lane:trade", "lane:farm", "teamfight:engage", "teamfight:protect", "teamfight:position", "draft:role", "augment:synergy" };

    private static string BuildEvidence(AiGameContext c, IReadOnlyList<string> fields) => string.Join("；", fields.Select(f => f switch
    {
        "champion" => "英雄：" + c.MyChampion, "role" => "分路：" + c.MyRole,
        "allies" => "己方：" + string.Join("、", c.AlliedChampions), "enemies" => "敌方：" + string.Join("、", c.EnemyChampions),
        "items" => "已购：" + (c.CurrentItems.Count == 0 ? "暂无" : string.Join("、", c.CurrentItems)),
        "gold" => "金币：" + c.CurrentGold, "time" => "时间：" + c.GameTimeText,
        "matchup" => "玩家确认对线：" + c.MatchupChampion, "augments" => "已确认候选：" + string.Join("、", c.ConfirmedAugmentOptions), _ => ""
    }));

    private static IReadOnlyList<CoachRecommendation> ParseContent(string content, DateTimeOffset now)
    {
        string normalized = RemoveMarkdownFence(content).Trim();
        if (LooksLikeJson(normalized))
        {
            IReadOnlyList<CoachRecommendation> cards = ParseJsonCards(normalized, now);
            if (cards.Count > 0) return cards;
            return [CreateTextCard("云端建议格式不完整，请稍后刷新重试。", now) with { IsFormatError = true }];
        }

        string readable = string.IsNullOrWhiteSpace(normalized)
            ? "云端服务没有返回可展示的补充建议。"
            : normalized;
        var sections = Regex.Split(readable, @"(?m)(?=^【[^】\r\n]+】)").Where(s => !string.IsNullOrWhiteSpace(s)).Take(3).ToArray();
        if (sections.Length <= 1 || !sections[0].StartsWith('【')) return [CreateTextCard(readable, now)];
        return sections.Select(section =>
        {
            var heading = Regex.Match(section, @"^【([^】]+)】([^\r\n]*)");
            if (!heading.Success) return CreateTextCard(section.Trim(), now);
            string lines = section[heading.Length..].Trim();
            var condition = Regex.Match(lines, @"(?m)^条件[：:](.*)$");
            return CreateTextCard(lines, now) with { Title = heading.Groups[2].Value.Trim(), Category = heading.Groups[1].Value,
                Conditions = condition.Success ? condition.Groups[1].Value.Trim() : "结合当前局势判断" };
        }).ToArray();
    }

    /// <summary>将 JSON 推荐内容解析为推荐卡片集合。</summary>
    private static IReadOnlyList<CoachRecommendation> ParseJsonCards(string json, DateTimeOffset now)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement cards = document.RootElement.ValueKind == JsonValueKind.Object &&
                                document.RootElement.TryGetProperty("recommendations", out JsonElement nested)
                ? nested : document.RootElement;
            if (cards.ValueKind == JsonValueKind.Array)
            {
                var parsed = new List<CoachRecommendation>();
                foreach (JsonElement card in cards.EnumerateArray())
                {
                    if (card.ValueKind != JsonValueKind.Object || parsed.Count >= 3) continue;
                    CoachRecommendation? recommendation = ParseCard(card, now, parsed.Count);
                    if (recommendation != null) parsed.Add(recommendation);
                }
                if (parsed.Count > 0) return parsed;
            }
        }
        catch (JsonException)
        {
            // 兼容旧模型生成到 token 上限时留下的半截 JSON：只恢复完整对象。
        }

        var recovered = new List<CoachRecommendation>();
        foreach (Match match in Regex.Matches(json, @"\{[^{}]*\}"))
        {
            if (recovered.Count >= 3) break;
            try
            {
                using JsonDocument document = JsonDocument.Parse(match.Value);
                CoachRecommendation? card = ParseCard(document.RootElement, now, recovered.Count);
                if (card != null) recovered.Add(card);
            }
            catch (JsonException) { }
        }
        return recovered;
    }

    /// <summary>将单条推荐 JSON 转换为领域推荐卡片。</summary>
    private static CoachRecommendation? ParseCard(JsonElement card, DateTimeOffset now, int index)
    {
        string title = GetText(card, "title");
        string body = GetText(card, "body");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body)) return null;
        string category = GetText(card, "category");
        string evidence = GetText(card, "evidence");
        return new CoachRecommendation(
            $"cloud-{StableHash(title + body)}-{index}",
            string.IsNullOrWhiteSpace(category) ? "云端补充" : category[..Math.Min(category.Length, 24)],
            ParsePriority(GetText(card, "priority")),
            title[..Math.Min(title.Length, 72)],
            body,
            string.IsNullOrWhiteSpace(evidence) ? "基于当前可见对局信息。" : evidence,
            RecommendationSource.CloudAi,
            now.AddMinutes(2))
        {
            GeneratedAt = now,
            Conditions = GetText(card, "conditions"),
            InvalidWhen = GetText(card, "invalidWhen") is { Length: > 0 } invalid ? invalid : "装备、阵容或阶段变化时失效",
            ActionKey = GetText(card, "actionKey"),
            EvidenceFields = GetArray(card, "evidenceFields"),
            ReferencedItems = GetArray(card, "referencedItems"),
            IsFormatError = card.TryGetProperty("evidenceFields", out _) && GetArray(card, "evidenceFields").Count == 0
        };
    }

    /// <summary>将纯文本响应包装为可展示的推荐卡片。</summary>
    private static CoachRecommendation CreateTextCard(string text, DateTimeOffset now) => new(
        "cloud-text-" + StableHash(text),
        "云端补充",
        RecommendationPriority.Info,
        "云端局势补充",
        text,
        "",
        RecommendationSource.CloudAi,
        now.AddMinutes(2)) { GeneratedAt = now };

    private static IReadOnlyList<string> GetArray(JsonElement card, string property) =>
        card.TryGetProperty(property, out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).Take(12).ToArray() : [];

    /// <summary>初步判断响应文本是否为 JSON 格式。</summary>
    private static bool LooksLikeJson(string text) => text.StartsWith('{') || text.StartsWith('[') ||
        Regex.IsMatch(text, "\\\"recommendations\\\"\\s*:");

    /// <summary>去除响应外围的 Markdown 代码块标记。</summary>
    private static string RemoveMarkdownFence(string text)
    {
        string trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;
        int firstLineEnd = trimmed.IndexOf('\n');
        if (firstLineEnd < 0) return trimmed;
        int lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return lastFence > firstLineEnd ? trimmed[(firstLineEnd + 1)..lastFence] : trimmed[(firstLineEnd + 1)..];
    }

    /// <summary>读取推荐 JSON 中的字符串字段，缺失或类型不符时返回空文本。</summary>
    private static string GetText(JsonElement card, string property) =>
        card.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    /// <summary>将推荐中的优先级文本转换为内部等级。</summary>
    private static RecommendationPriority ParsePriority(string value) => value.Trim().ToLowerInvariant() switch
    {
        "important" or "high" or "高" => RecommendationPriority.Important,
        "attention" or "medium" or "中" => RecommendationPriority.Attention,
        _ => RecommendationPriority.Info
    };

    /// <summary>为推荐内容生成稳定的摘要标识。</summary>
    private static string StableHash(string value) => unchecked((uint)StringComparer.Ordinal.GetHashCode(value)).ToString("X");
}
