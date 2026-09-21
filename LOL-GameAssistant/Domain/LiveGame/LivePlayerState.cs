namespace LOL_GameAssistant.Domain.LiveGame;

/// <summary>仅包含当前玩家公开可见的本机实时数据。</summary>
public sealed record LivePlayerState(int CurrentGold, IReadOnlyList<string> Items);
