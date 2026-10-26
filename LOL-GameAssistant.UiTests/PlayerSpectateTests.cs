using System.Net;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using Newtonsoft.Json;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class PlayerSpectateTests
{
    [Fact]
    public async Task SubmitsOnlySelectedPuuidWithoutRequiringGameId()
    {
        int calls = 0;
        var sender = new LcuHttpRequestSender((method, endpoint, body, token) =>
        {
            calls++;
            Assert.Equal("POST", method);
            Assert.Equal("/lol-spectator/v3/buddy/spectate", endpoint);
            Assert.Equal(new[] { "non-friend-puuid" }, JsonConvert.DeserializeObject<string[]>(body!));
            return Task.FromResult(new LcuHttpResult(null, HttpStatusCode.NoContent, LcuHttpFailure.None));
        });
        var result = await new LcuPlayerSpectateService(sender).LaunchAsync("non-friend-puuid");
        Assert.True(result.Accepted);
        Assert.Contains("HTTP 204", result.Message);
        Assert.Contains("请确认是否进入观战画面", result.Message);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RejectionPreservesStatusWithoutClaimingAllSpectatingIsUnavailable()
    {
        var sender = new LcuHttpRequestSender((method, endpoint, body, token) =>
            Task.FromResult(new LcuHttpResult(null, HttpStatusCode.Forbidden, LcuHttpFailure.HttpStatus)));
        var result = await new LcuPlayerSpectateService(sender).LaunchAsync("non-friend-puuid");
        Assert.False(result.Accepted);
        Assert.Contains("HTTP 403", result.Message);
        Assert.Contains("可能", result.Message);
    }

    [Theory]
    [InlineData(LcuHttpFailure.NoCredentials, "登录")]
    [InlineData(LcuHttpFailure.Timeout, "超时")]
    [InlineData(LcuHttpFailure.Network, "无法连接")]
    public async Task ConnectionFailuresHaveActionableMessages(LcuHttpFailure failure, string expected)
    {
        var sender = new LcuHttpRequestSender((method, endpoint, body, token) =>
            Task.FromResult(new LcuHttpResult(null, null, failure)));
        var result = await new LcuPlayerSpectateService(sender).LaunchAsync("target");
        Assert.False(result.Accepted);
        Assert.Contains(expected, result.Message);
    }

    [Fact]
    public async Task MissingTargetAndCancellationNeverSendRequests()
    {
        var sender = new LcuHttpRequestSender((method, endpoint, body, token) =>
            throw new InvalidOperationException("Should not send"));
        var service = new LcuPlayerSpectateService(sender);
        Assert.False((await service.LaunchAsync(" ")).Accepted);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.LaunchAsync("target", cancellation.Token));
    }
}
