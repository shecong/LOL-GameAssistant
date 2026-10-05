using LOL_GameAssistant.Domain.Friends;

namespace LOL_GameAssistant.Application.Friends;

/// <summary>读取当前客户端好友目录的应用服务。</summary>
public interface IFriendDirectoryService
{
    /// <summary>读取当前召唤师的好友列表。</summary>
    Task<IReadOnlyList<FriendProfile>> GetFriendsAsync(CancellationToken cancellationToken = default);
}