using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LOL_GameAssistant.Application.ApplicationInfo;
using LOL_GameAssistant.Domain.ApplicationInfo;
using LOL_GameAssistant.Infrastructure.ApplicationInfo;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class ServerUpdateReleaseServiceTests
{
    [Fact]
    public async Task StaticServerFindsVersionWithoutContactingGithub()
    {
        using var fixture = new Fixture();
        var release = await fixture.Service.GetLatestAsync();
        Assert.Equal(new Version(99, 0, 0), release!.Version);
        Assert.Equal($"https://updates.example.com/app/v99.0.0/{UpdatePlatform.Current}/update.json", release.ServerManifestUrl!.AbsoluteUri);
        Assert.Equal("https://updates.example.com/app/update.json", Assert.Single(fixture.Requests).AbsoluteUri);
        Assert.Equal(0, fixture.Github.Calls);
        Assert.True(release.PreferServerDownload);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("invalid-json")]
    [InlineData("invalid-schema")]
    [InlineData("oversized")]
    public async Task ServerFailureFallsBackToGithubAndKeepsBothDownloadSources(string failure)
    {
        using var fixture = new Fixture();
        fixture.Failure = failure;
        var release = await fixture.Service.GetLatestAsync();
        Assert.Equal(new Version(98, 0, 0), release!.Version);
        Assert.Equal($"https://updates.example.com/app/v98.0.0/{UpdatePlatform.Current}/update.json", release.ServerManifestUrl!.AbsoluteUri);
        Assert.Equal(1, fixture.Github.Calls);
        Assert.False(release.PreferServerDownload);
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://public.example.com/app/")]
    [InlineData("https://user:secret@example.com/")]
    [InlineData("invalid")]
    public async Task EmptyOrInvalidServerUsesOriginalGithubFlow(string address)
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.ConfigPath, JsonSerializer.Serialize(new { serverBaseUrl = address }));
        var release = await fixture.Service.GetLatestAsync();
        Assert.Null(release!.ServerManifestUrl);
        Assert.Empty(fixture.Requests);
        Assert.Equal(1, fixture.Github.Calls);
    }

    [Fact]
    public async Task ExplicitHttpOptInSupportsIisIpAndFlowsToPackageDownload()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.ConfigPath, "{\"serverBaseUrl\":\"http://159.75.148.241/\",\"allowHttp\":true}");
        var release = await fixture.Service.GetLatestAsync();
        Assert.True(release!.AllowHttpServer);
        Assert.Equal($"http://159.75.148.241/v99.0.0/{UpdatePlatform.Current}/update.json", release.ServerManifestUrl!.AbsoluteUri);
        Assert.Equal($"http://159.75.148.241/v99.0.0/{UpdatePlatform.Current}/{UpdatePlatform.Package(UpdatePlatform.Current)}",
            UpdatePackageService.AssetUrl(release, "LOL-GameAssistant-win-x64.zip", useServer: true).AbsoluteUri);
        Assert.Throws<InvalidDataException>(() => UpdatePackageService.AssetUrl(release with { AllowHttpServer = false }, "update.json", useServer: true));
        Assert.Equal(0, fixture.Github.Calls);
    }

    [Fact]
    public async Task CancellationDoesNotStartGithubFallback()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.GetLatestAsync(cancellation.Token));
        Assert.Equal(0, fixture.Github.Calls);
    }

    [Fact]
    public async Task ReadsMetadataThroughRealLoopbackHttpConnection()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var fixture = new Fixture();
        File.WriteAllText(fixture.ConfigPath, JsonSerializer.Serialize(new { serverBaseUrl = $"http://127.0.0.1:{port}/app/" }));
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var service = new ServerUpdateReleaseService(client, fixture.Github, fixture.ConfigPath);
        var responding = Task.Run(async () =>
        {
            using var connection = await listener.AcceptTcpClientAsync();
            await using var stream = connection.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            Assert.Equal("GET /app/update.json HTTP/1.1", await reader.ReadLineAsync());
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
            byte[] body = Encoding.UTF8.GetBytes(Fixture.ManifestJson);
            byte[] header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header);
            await stream.WriteAsync(body);
        });
        var release = await service.GetLatestAsync();
        await responding.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal($"http://127.0.0.1:{port}/app/v99.0.0/{UpdatePlatform.Current}/update.json", release!.ServerManifestUrl!.AbsoluteUri);
        Assert.Equal(0, fixture.Github.Calls);
    }

    private sealed class Fixture : IDisposable
    {
        internal static string ManifestJson => JsonSerializer.Serialize(new UpdateIndex(1, "99.0.0"));
        public string ConfigPath { get; } = Path.Combine(Path.GetTempPath(), "update-source-" + Guid.NewGuid().ToString("N") + ".json");
        public string? Failure { get; set; }
        public List<Uri> Requests { get; } = [];
        public GithubStub Github { get; } = new();
        public ServerUpdateReleaseService Service { get; }
        private readonly HttpClient _client;
        public Fixture()
        {
            File.WriteAllText(ConfigPath, "{\"serverBaseUrl\":\"https://updates.example.com/app/\"}");
            _client = new HttpClient(new Handler(request =>
            {
                Requests.Add(request.RequestUri!);
                return new HttpResponseMessage(Failure == "unavailable" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
                {
                    Content = new StringContent(Failure switch
                    {
                        "invalid-json" => "invalid",
                        "invalid-schema" => JsonSerializer.Serialize(new UpdateIndex(2, "99.0.0")),
                        "oversized" => new string(' ', 65537),
                        _ => ManifestJson
                    })
                };
            }));
            Service = new(_client, Github, ConfigPath);
        }
        public void Dispose() { _client.Dispose(); File.Delete(ConfigPath); }
    }

    private sealed class GithubStub : IUpdateReleaseService
    {
        public int Calls { get; private set; }
        public Task<UpdateRelease?> GetLatestAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<UpdateRelease?>(new(new Version(98, 0, 0), "v98.0.0", "https://github.com/releases", ""));
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
