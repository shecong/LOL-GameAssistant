namespace LOL_GameAssistant.Domain.LiveGame;

/// <summary>同一次 Live Client Data API 采样得到的玩家状态与游戏时间，避免重复请求本机端点。</summary>
public sealed record LiveClientGameSnapshot(
    int CurrentGold,
    IReadOnlyList<string> Items,
    int GameTimeSeconds,
    string? GameMode);