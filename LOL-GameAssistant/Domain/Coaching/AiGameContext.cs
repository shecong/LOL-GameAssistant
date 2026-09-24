namespace LOL_GameAssistant.Domain.Coaching;

/// <summary>
/// 发送给 AI 前已最小化、去敏的本局上下文；
/// 不包含 LCU 令牌、玩家标识或任何隐藏战场信息。
/// </summary>
public sealed class AiGameContext
{
    public string Phase { get; init; } = "未知";
    public string Mode { get; init; } = "峡谷对局";
    /// <summary>LCU 原始游戏模式，用于映射 OP.GG 的 ranked/aram/arena 等接口。</summary>
    public string GameMode { get; init; } = "CLASSIC";
    public int QueueId { get; init; }
    public string MyChampion { get; init; } = "未选择英雄";
    public int MyChampionId { get; init; }
    public string MyRole { get; init; } = "未知位置";
    public int CurrentGold { get; init; }
    public int GameTimeSeconds { get; init; }

    /// <summary>本机 Live Client Data API 是否成功提供本轮局内状态。</summary>
    public bool HasLiveClientData { get; init; }

    public IReadOnlyList<string> CurrentItems { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AlliedChampions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> EnemyChampions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<int> EnemyChampionIds { get; init; } = Array.Empty<int>();
    public string LaneKnowledge { get; init; } = "暂无匹配到的专项知识点。";

    public bool IsAram => Mode.Contains("大乱斗", StringComparison.Ordinal);

    public string GameTimeText => $"{Math.Max(0, GameTimeSeconds) / 60}:{Math.Max(0, GameTimeSeconds) % 60:D2}";

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
        游戏时间：{GameTimeText}
        当前金币：{CurrentGold}
        本机实时数据可用：{HasLiveClientData}
        已购装备：{items}
        已知己方英雄：{allies}
        已知敌方英雄：{enemies}
        本地对线知识：{LaneKnowledge}
        """;
    }
}
