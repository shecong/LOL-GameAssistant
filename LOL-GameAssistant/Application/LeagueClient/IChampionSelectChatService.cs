namespace LOL_GameAssistant.Application.LeagueClient;

/// <summary>向当前英雄选择聊天会话发送一条消息；不适用于已经进入游戏后的聊天窗口。</summary>
public interface IChampionSelectChatService
{
    Task<ChampionSelectChatResult> SendAsync(string message, CancellationToken cancellationToken = default);
}

public sealed record ChampionSelectChatResult(bool Succeeded, string Message)
{
    public static ChampionSelectChatResult Success() => new(true, "已发送到选人聊天窗口。");
    public static ChampionSelectChatResult Failure(string message) => new(false, message);
}
