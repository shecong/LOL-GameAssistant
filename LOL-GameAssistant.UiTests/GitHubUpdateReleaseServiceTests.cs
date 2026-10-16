using System.Net;
using LOL_GameAssistant.Infrastructure.ApplicationInfo;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class GitHubUpdateReleaseServiceTests
{
    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "6.0")]
    [InlineData(HttpStatusCode.TooManyRequests, "v1.2.3")]
    public async Task LimitedApiFallsBackToLatestPageAndCachesSuccessfulResult(HttpStatusCode status, string tag)
    {
        var requests = new List<Uri>();
        using var client = new HttpClient(new StubHandler(request =>
        {
            requests.Add(request.RequestUri!);
            return request.RequestUri!.Host == "api.github.com"
                ? new HttpResponseMessage(status)
                : PageResponse($"https://github.com/shecong/LOL-GameAssistant/releases/tag/{tag}");
        }));
        var service = new GitHubUpdateReleaseService(client);
        var result = await service.GetLatestAsync();
        Assert.NotNull(result);
        Assert.Equal(tag, result.TagName);
        Assert.Equal(Version.Parse(tag.TrimStart('v')), result.Version);
        Assert.EndsWith("/tag/" + tag, result.ReleaseUrl);
        Assert.Same(result, await service.GetLatestAsync());
        Assert.Equal(2, requests.Count);
        Assert.Equal("https://github.com/shecong/LOL-GameAssistant/releases/latest", requests[1].AbsoluteUri);
    }

    [Fact]
    public async Task SuccessfulApiPreservesNotesWithoutRequestingPage()
    {
        int calls = 0;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"tag_name":"v1.2.3","html_url":"https://github.com/shecong/LOL-GameAssistant/releases/tag/v1.2.3","body":"Release notes"}""")
            };
        }));
        var result = await new GitHubUpdateReleaseService(client).GetLatestAsync();
        Assert.Equal("Release notes", result!.ReleaseNotes);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("https://github.com/shecong/LOL-GameAssistant/releases")]
    [InlineData("https://github.com/shecong/LOL-GameAssistant/releases/tag/preview")]
    [InlineData("https://example.com/shecong/LOL-GameAssistant/releases/tag/v99.0")]
    public async Task UnexpectedPageDoesNotInventVersion(string finalUrl)
    {
        using var client = new HttpClient(new StubHandler(request => request.RequestUri!.Host == "api.github.com"
            ? new HttpResponseMessage(HttpStatusCode.Forbidden)
            : PageResponse(finalUrl)));
        Assert.Null(await new GitHubUpdateReleaseService(client).GetLatestAsync());
    }

    [Fact]
    public async Task CallerCancellationDoesNotTriggerFallback()
    {
        int calls = 0;
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(new StubHandler(_ =>
        {
            calls++;
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new GitHubUpdateReleaseService(client).GetLatestAsync(cancellation.Token));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ApiTimeoutFallsBackToPage()
    {
        using var client = new HttpClient(new StubHandler(request => request.RequestUri!.Host == "api.github.com"
            ? throw new TaskCanceledException("API timeout")
            : PageResponse("https://github.com/shecong/LOL-GameAssistant/releases/tag/v2.0.0")));
        var release = await new GitHubUpdateReleaseService(client).GetLatestAsync();
        Assert.Equal(new Version(2, 0, 0), release!.Version);
    }

    private static HttpResponseMessage PageResponse(string url) => new(HttpStatusCode.OK)
    {
        RequestMessage = new HttpRequestMessage(HttpMethod.Get, url)
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
