using LOL_GameAssistant.Domain.Friends;

namespace LOL_GameAssistant.Application.Friends;

/// <summary>读取当前客户端好友目录的应用服务。</summary>
public interface IFriendDirectoryService
{
    Task<IReadOnlyList<FriendProfile>> GetFriendsAsync(CancellationToken cancellationToken = default);
}