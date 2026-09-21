namespace LOL_GameAssistant.Domain.Players;

/// <summary>
/// 召唤师资料领域模型。
/// 统一数字类型，避免表现层处理 LCU 中字符串形式的等级和头像 ID。
/// </summary>
public sealed record PlayerProfile(
    string Puuid,
    string GameName,
    string TagLine,
    int ProfileIconId,
    int SummonerLevel,
    int XpSinceLastLevel,
    int XpUntilNextLevel)
{
    /// <summary>供界面和复制功能使用的 Riot ID。</summary>
    public string RiotId => string.IsNullOrWhiteSpace(TagLine) ? GameName : $"{GameName}#{TagLine}";
}
