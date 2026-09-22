namespace LOL_GameAssistant.Helper;

/// <summary>Built-in one-click message presets. Translation is intentionally not performed in the background.</summary>
public static class QuickMessageTemplates
{
    private static readonly IReadOnlyDictionary<string, string> Templates = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["中文"] = "我去支援，请注意地图。",
        ["English"] = "I'm coming to help. Please watch the map.",
        ["日本語"] = "援護に向かいます。マップに注意してください。",
        ["한국어"] = "지원 갈게요. 맵을 봐 주세요."
    };

    public static bool TryGet(string? language, out string text) =>
        Templates.TryGetValue(language ?? string.Empty, out text!);
}