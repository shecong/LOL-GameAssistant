using LOL_GameAssistant.Domain.Coaching;
using System.Text.Json;

namespace LOL_GameAssistant.Infrastructure.Ai;

/// <summary>把 AI 文本约束为可验证的建议卡片；服务不遵守 JSON 时保留为一张 AI 文本卡。</summary>
internal static class CloudRecommendationParser
{
    public static IReadOnlyList<CoachRecommendation> Parse(string content, DateTimeOffset now)
    {
        string normalized = RemoveMarkdownFence(content).Trim();
        try
        {
            using JsonDocument document = JsonDocument.Parse(normalized);
            JsonElement cards = document.RootElement.TryGetProperty("recommendations", out var nested)
                ? nested
                : document.RootElement;
            if (cards.ValueKind != JsonValueKind.Array) throw new JsonException("recommendations 不是数组。");

            var parsed = new List<CoachRecommendation>();
            int index = 0;
            foreach (JsonElement card in cards.EnumerateArray())
            {
                if (card.ValueKind != JsonValueKind.Object || parsed.Count >= 3) continue;
                string title = GetText(card, "title");
                string body = GetText(card, "body");
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body)) continue;

                string category = GetText(card, "category");
                string evidence = GetText(card, "evidence");
                string priority = GetText(card, "priority");
                parsed.Add(new CoachRecommendation(
                    $"cloud-{StableHash(title + body)}-{index++}",
                    string.IsNullOrWhiteSpace(category) ? "云端补充" : category[..Math.Min(category.Length, 24)],
                    ParsePriority(priority),
                    title[..Math.Min(title.Length, 72)],
                    body[..Math.Min(body.Length, 360)],
                    string.IsNullOrWhiteSpace(evidence) ? "基于当前可见对局信息。" : evidence[..Math.Min(evidence.Length, 180)],
                    RecommendationSource.CloudAi,
                    now.AddMinutes(2)));
            }

            if (parsed.Count > 0) return parsed;
        }
        catch (JsonException)
        {
            // 部分兼容服务不稳定地遵从 JSON 格式，保留 AI 返回的可读内容。
        }

        string fallback = string.IsNullOrWhiteSpace(content) ? "云端服务没有返回可展示的补充建议。" : content.Trim();
        return new[]
        {
            new CoachRecommendation(
                "cloud-text-" + StableHash(fallback),
                "云端补充",
                RecommendationPriority.Info,
                "云端局势补充",
                fallback[..Math.Min(fallback.Length, 600)],
                "云端模型基于当前可见信息生成。",
                RecommendationSource.CloudAi,
                now.AddMinutes(2))
        };
    }

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
