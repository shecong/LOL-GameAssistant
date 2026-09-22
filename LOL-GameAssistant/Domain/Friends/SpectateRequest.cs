namespace LOL_GameAssistant.Domain.Friends;

/// <summary>
/// 好友观战领域请求。
/// 仅描述“观战哪位好友的哪一场对局”，不依赖 WinForms 或 LCU 的传输细节。
/// </summary>
public sealed record SpectateRequest(string FriendPuuid, long GameId);

/// <summary>观战用例执行后的领域结果。</summary>
public sealed record SpectateResult(bool Succeeded, string Message);