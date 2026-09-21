namespace LOL_GameAssistant.Domain.Coaching;

/// <summary>
/// 发送给 AI 前已最小化、去敏的本局上下文；
/// 不包含 LCU 令牌、玩家标识或任何隐藏战场信息。
/// </summary>
public sealed class AiGameContext
{
    public string Phase { get; init; } = "未知";
    public string Mode { get; init; } = "峡谷对局";
    public string MyChampion { get; init; } = "未选择英雄";
    public string MyRole { get; init; } = "未知位置";
    public int CurrentGold { get; init; }
    public IReadOnlyList<string> CurrentItems { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AlliedChampions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> EnemyChampions { get; init; } = Array.Empty<string>();
    public string LaneKnowledge { get; init; } = "暂无匹配到的专项知识点。";

    public bool IsAram => Mode.Contains("大乱斗", StringComparison.Ordinal);

    /// <summary>将领域上下文规整为模型请求使用的文本，不暴露身份和认证信息。</summary>
    public string ToPromptText()
    {
        string items = CurrentItems.Count == 0 ? "暂无" : string.Join("、", CurrentItems);
        string allies = AlliedChampions.Count == 0 ? "暂无" : string.Join("、", AlliedChampions);
        string enemies = EnemyChampions.Count == 0 ? "暂无" : string.Join("、", EnemyChampions);
        return $"""
        游戏阶段：{Phase}
        模式：{Mode}
        我的英雄：{MyChampion}
        位置：{MyRole}
        当前金币：{CurrentGold}
        已购装备：{items}
        已知己方英雄：{allies}
        已知敌方英雄：{enemies}
        本地对线知识：{LaneKnowledge}
        """;
    }
}
