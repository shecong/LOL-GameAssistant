namespace LOL_GameAssistant.Domain.Friends;

/// <summary>
/// 好友观战领域请求。
/// 通过好友 PUUID 选择观战目标；对局 ID 为可选的活动信息，不作为观战前提。
/// </summary>
public sealed record SpectateRequest(string FriendPuuid, long GameId = 0);

/// <summary>观战用例执行后的领域结果。</summary>
public sealed record SpectateResult(bool Succeeded, string Message);
