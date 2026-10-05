namespace LOL_GameAssistant.Application.LeagueClient;

/// <summary>向当前英雄选择聊天会话发送一条消息；不适用于已经进入游戏后的聊天窗口。</summary>
public interface IChampionSelectChatService
{
    /// <summary>向当前服务负责的聊天通道提交消息。</summary>
    Task<ChampionSelectChatResult> SendAsync(string message, CancellationToken cancellationToken = default);
}

/// <summary>选人聊天发送操作的状态和说明。</summary>
public sealed record ChampionSelectChatResult(bool Succeeded, string Message)
{
    /// <summary>构建成功结果，向调用方返回操作状态。</summary>
    public static ChampionSelectChatResult Success() => new(true, "已发送到选人聊天窗口。");
    /// <summary>构建失败结果，向调用方保留可展示的原因。</summary>
    public static ChampionSelectChatResult Failure(string message) => new(false, message);
}