using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Players;
using Newtonsoft.Json;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>实验性非好友观战：只提交一次请求，由当前客户端判断权限和对局状态。</summary>
public sealed class LcuPlayerSpectateService(ILcuResultRequestSender requests) : IPlayerSpectateService
{
    internal const string Endpoint = "/lol-spectator/v3/buddy/spectate";

    public async Task<PlayerSpectateResult> LaunchAsync(string puuid, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(puuid)) return new(false, "请先查询一位玩家。");
        var result = await requests.SendAsync("POST", Endpoint,
            JsonConvert.SerializeObject(new[] { puuid }), cancellationToken).ConfigureAwait(false);
        string status = result.StatusCode is { } code ? $"HTTP {(int)code}" : "无 HTTP 响应";
        string message = result.Succeeded
            ? $"客户端已接受观战请求（{status}），请确认是否进入观战画面；非好友观战仍需实测。"
            : result.Failure switch
            {
                LcuFailure.NoCredentials => "未连接客户端，请先启动 LOL 并登录。",
                LcuFailure.Timeout => "观战请求超时，请检查客户端状态后重试。",
                LcuFailure.Network => "无法连接客户端，请检查客户端是否仍在运行。",
                _ => $"观战请求未被接受（{status}，{result.Failure}）。目标可能未在对局、限制观战，或当前客户端不支持非好友观战。"
            };
        LOL_GameAssistant.Helper.RuntimeDiagnostics.Report("玩家观战", result.Succeeded ? "请求已接受" : "请求失败",
            $"POST {Endpoint} · {status} · {result.Failure}");
        return new(result.Succeeded, message);
    }
}
