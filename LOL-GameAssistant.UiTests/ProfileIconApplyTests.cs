using System.Net;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class ProfileIconApplyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplySendsIconObjectToSummonerEndpoint(bool classified)
    {
        var sender = classified ? new ClassifiedSender() : new Sender();
        using var cancellation = new CancellationTokenSource();
        var result = await new LcuClientFeatureService(sender).SetProfileIconAsync(23, cancellation.Token);

        Assert.True(result.Succeeded);
        var call = Assert.Single(sender.Calls);
        Assert.Equal("PUT", call.Method);
        Assert.Equal("/lol-summoner/v1/current-summoner/icon", call.Endpoint);
        JObject payload = JObject.Parse(call.Body);
        Assert.Single(payload.Properties());
        Assert.Equal(JTokenType.Integer, payload["profileIconId"]!.Type);
        Assert.Equal(23, payload.Value<int>("profileIconId"));
        Assert.Equal(cancellation.Token, call.Token);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidIconDoesNotSendRequest(int iconId)
    {
        var sender = new ClassifiedSender();
        var result = await new LcuClientFeatureService(sender).SetProfileIconAsync(iconId);
        Assert.False(result.Succeeded);
        Assert.Empty(sender.Calls);
    }

    [Fact]
    public async Task RejectedIconPreservesHttpFailure()
    {
        var sender = new ClassifiedSender
        {
            Response = new(null, HttpStatusCode.BadRequest, LcuFailure.HttpStatus)
        };
        var result = await new LcuClientFeatureService(sender).SetProfileIconAsync(23);
        Assert.False(result.Succeeded);
        Assert.Equal(LcuFailure.HttpStatus, result.FailureKind);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("400", result.Message);
    }

    [Fact]
    public async Task LegacySenderFailureDoesNotReportSuccess()
    {
        var sender = new Sender { Accepted = false };
        var result = await new LcuClientFeatureService(sender).SetProfileIconAsync(23);
        Assert.False(result.Succeeded);
        Assert.Single(sender.Calls);
    }

    private class Sender : ILcuRequestSender
    {
        public List<(string Method, string Endpoint, string Body, CancellationToken Token)> Calls { get; } = [];
        public bool Accepted { get; init; } = true;
        public Task<bool> PutAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default)
        {
            Calls.Add(("PUT", endpoint, jsonBody, cancellationToken));
            return Task.FromResult(Accepted);
        }
        public Task<string?> GetStringAsync(string endpoint, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<byte[]?> GetBytesAsync(string endpoint, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> PostAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> PatchAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string endpoint, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ClassifiedSender : Sender, ILcuResultRequestSender
    {
        public LcuResponse<byte[]> Response { get; init; } = new([], HttpStatusCode.NoContent, LcuFailure.None);
        public Task<LcuResponse<byte[]>> SendAsync(string method, string endpoint, string? body = null, CancellationToken cancellationToken = default)
        {
            Calls.Add((method, endpoint, body ?? "", cancellationToken));
            return Task.FromResult(Response);
        }
    }
}
