using Newtonsoft.Json;

namespace LOL_GameAssistant.Infrastructure.Coaching;

/// <summary>
/// 可维护的对线知识库读取器。数据文件可随版本独立更新，无需改动程序逻辑。
/// </summary>
internal static class LocalLaneKnowledgeReader
{
    private const string KnowledgeFileName = "lane-knowledge.zh-CN.json";
    private static readonly object Sync = new();
    private static LaneKnowledgeDocument? _cached;

    /// <summary>根据对局上下文读取适用的本地对线建议。</summary>
    public static string GetAdvice(string champion, string role, string? matchup = null)
    {
        LaneKnowledgeDocument document = Load();
        string normalizedChampion = champion.Trim();
        string normalizedRole = role.Trim();
        string normalizedMatchup = matchup?.Trim() ?? "";

        LaneKnowledgeEntry? entry = document.Entries.FirstOrDefault(item =>
            string.Equals(item.Champion, normalizedChampion, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.Role, normalizedRole, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(normalizedMatchup) &&
            string.Equals(item.Matchup, normalizedMatchup, StringComparison.OrdinalIgnoreCase))
            ?? document.Entries.FirstOrDefault(item =>
                string.Equals(item.Champion, normalizedChampion, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Role, normalizedRole, StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrEmpty(item.Matchup))
            ?? document.Entries.FirstOrDefault(item =>
                string.Equals(item.Champion, "通用", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Role, normalizedRole, StringComparison.OrdinalIgnoreCase))
            ?? document.Entries.FirstOrDefault(item => item.Champion == "通用" && item.Role == "通用");

        return entry == null ? "关注兵线、视野与关键技能冷却，依据可见信息选择换血或回撤。" : entry.ToText();
    }

    /// <summary>复用已加载的对线知识，文件不可用时回退到内置默认内容。</summary>
    private static LaneKnowledgeDocument Load()
    {
        lock (Sync)
        {
            if (_cached != null) return _cached;
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", KnowledgeFileName);
            try
            {
                if (File.Exists(path))
                {
                    _cached = JsonConvert.DeserializeObject<LaneKnowledgeDocument>(File.ReadAllText(path));
                }
            }
            catch
            {
                // 文件临时损坏时仍使用最小默认知识，不阻断助手。
            }

            return _cached ??= LaneKnowledgeDocument.Default();
        }
    }
}

/// <summary>本地对线知识文件的整体结构。</summary>
internal sealed class LaneKnowledgeDocument
{
    [JsonProperty("version")]
    public string Version { get; set; } = "1";

    [JsonProperty("entries")]
    public List<LaneKnowledgeEntry> Entries { get; set; } = new();

    /// <summary>构建本地知识配置缺失时使用的默认内容。</summary>
    public static LaneKnowledgeDocument Default() => new()
    {
        Entries = new List<LaneKnowledgeEntry>
        {
            new() { Champion = "通用", Role = "通用", Notes = new() { "前两波兵线优先保证补刀，再根据双方关键技能是否进入冷却决定换血。", "河道与野区入口缺少视野时，兵线不要压得过深。", "回城前观察经济是否足以完成关键组件，并预留兵线处理时间。" } }
        }
    };
}

/// <summary>按英雄或场景配置的单条对线知识。</summary>
internal sealed class LaneKnowledgeEntry
{
    [JsonProperty("champion")]
    public string Champion { get; set; } = "通用";

    [JsonProperty("role")]
    public string Role { get; set; } = "通用";

    [JsonProperty("matchup")]
    public string? Matchup { get; set; }

    [JsonProperty("notes")]
    public List<string> Notes { get; set; } = new();

    /// <summary>将知识条目组织为可展示的建议文本。</summary>
    public string ToText() => Notes.Count == 0 ? "暂无专项知识点。" : string.Join("；", Notes);
}