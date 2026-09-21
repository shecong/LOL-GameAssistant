using LOL_GameAssistant.Domain.Friends;

namespace LOL_GameAssistant.Application.Friends;

/// <summary>
/// 好友观战应用服务契约。
/// 表现层只依赖此接口，避免窗体直接了解 LCU 的接口路径与请求体。
/// </summary>
public interface IFriendSpectateService
{
    Task<SpectateResult> LaunchAsync(SpectateRequest request, CancellationToken cancellationToken = default);
}
