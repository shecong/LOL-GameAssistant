using LOL_GameAssistant.Application.Friends;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Domain.Friends;
using Newtonsoft.Json;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>
/// 通过本机 LCU 发起好友观战。
/// LCU 认证与 HTTPS 细节统一由 HttpClientHelper 承担，应用层不接触 token。
/// </summary>
public sealed class LcuFriendSpectateService : IFriendSpectateService
{
    private readonly ILcuRequestSender _requestSender;

    public LcuFriendSpectateService(ILcuRequestSender requestSender)
    {
        _requestSender = requestSender;
    }

    public async Task<SpectateResult> LaunchAsync(SpectateRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FriendPuuid) || request.GameId <= 0)
            return new SpectateResult(false, "该好友未提供可观战的对局信息。");

        bool succeeded = await _requestSender.PostAsync(
            // buddy/spectate 由客户端用好友 PUUID 获取完整观战凭据，
            // 不需要表现层自行猜测 spectatorKey、队列或观战模式。
            "/lol-spectator/v3/buddy/spectate",
            JsonConvert.SerializeObject(new[] { request.FriendPuuid }),
            cancellationToken).ConfigureAwait(false);

        if (!succeeded)
            return new SpectateResult(false, "观战启动失败：好友可能已结束对局、关闭观战或客户端暂不可用。");

        return new SpectateResult(true, "已向 LOL 客户端发起观战请求。");
    }
}