using LOL_GameAssistant.Domain.Coaching;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LOL_GameAssistant.Infrastructure.Ai;

/// <summary>兼容旧版 JSON 建议，并保证任何异常响应都不会把 JSON 原文直接展示给用户。</summary>
internal static class CloudRecommendationParser
{
    public static IReadOnlyList<CoachRecommendation> Parse(string content, DateTimeOffset now)
    {
        string normalized = RemoveMarkdownFence(content).Trim();
        if (LooksLikeJson(normalized))
        {
            IReadOnlyList<CoachRecommendation> cards = ParseJsonCards(normalized, now);
            if (cards.Count > 0) return cards;
            return [CreateTextCard("云端建议格式不完整，请稍后刷新重试。", now)];
        }

        string readable = string.IsNullOrWhiteSpace(normalized)
            ? "云端服务没有返回可展示的补充建议。"
            : normalized;
        return [CreateTextCard(readable, now)];
    }

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
            now.AddMinutes(2));
    }

    private static CoachRecommendation CreateTextCard(string text, DateTimeOffset now) => new(
        "cloud-text-" + StableHash(text),
        "云端补充",
        RecommendationPriority.Info,
        "云端局势补充",
        text,
        "",
        RecommendationSource.CloudAi,
        now.AddMinutes(2));

    private static bool LooksLikeJson(string text) => text.StartsWith('{') || text.StartsWith('[') ||
        Regex.IsMatch(text, "\\\"recommendations\\\"\\s*:");

    private static string RemoveMarkdownFence(string text)
    {
        string trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;
        int firstLineEnd = trimmed.IndexOf('\n');
        if (firstLineEnd < 0) return trimmed;
        int lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return lastFence > firstLineEnd ? trimmed[(firstLineEnd + 1)..lastFence] : trimmed[(firstLineEnd + 1)..];
    }

    private static string GetText(JsonElement card, string property) =>
        card.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static RecommendationPriority ParsePriority(string value) => value.Trim().ToLowerInvariant() switch
    {
        "important" or "high" or "高" => RecommendationPriority.Important,
        "attention" or "medium" or "中" => RecommendationPriority.Attention,
        _ => RecommendationPriority.Info
    };

    private static string StableHash(string value) => unchecked((uint)StringComparer.Ordinal.GetHashCode(value)).ToString("X");
}
