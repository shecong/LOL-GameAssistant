using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Infrastructure.Skins;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class ResourceDownloadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "resource-download-tests-" + Guid.NewGuid().ToString("N"));
    private static byte[] Wad()
    {
        var bytes = new byte[305]; bytes[0] = (byte)'R'; bytes[1] = (byte)'W'; bytes[2] = 3;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(268), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(280), 304);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(284), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(288), 1); return bytes;
    }
    private static readonly ResourceRepositoryOptions Options = new() { Owner = "test-owner", Repository = "test-repo", AccessToken = "test-only-token" };
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    private static ResourceRepositoryEntry Entry(string sha = "") => new("源计划：净化.wad.client", "暗夜猎手-薇恩/源计划：净化.wad.client", false, 305, sha);
    private GiteeResourceSkinRepository Repo(Func<HttpRequestMessage, HttpResponseMessage> callback) => new(_root, new Handler(callback));

    [Fact]
    public async Task ListsChineseDirectoriesAndWadsUsingHeaderTokenWithoutQuerySecrets()
    {
        using var repo = Repo(request =>
        {
            Assert.Equal("test-only-token", request.Headers.Authorization!.Parameter);
            Assert.Equal("token", request.Headers.Authorization.Scheme);
            Assert.DoesNotContain("test-only-token", request.RequestUri!.ToString());
            Assert.Contains("/contents/", request.RequestUri.AbsolutePath);
            return Json(new[] {
                new { name = "暗夜猎手-薇恩", path = "暗夜猎手-薇恩", type = "dir", size = (long?)null, sha = "" },
                new { name = "test.wad.client", path = "test.wad.client", type = "file", size = (long?)305, sha = "" },
                new { name = "HID.dll", path = "HID.dll", type = "file", size = (long?)2000, sha = "" }
            });
        });
        var items = await repo.ListAsync(Options, ""); Assert.Equal(2, items.Count);
        Assert.True(items[0].IsDirectory); Assert.Equal("暗夜猎手-薇恩", items[0].Name);
        Assert.DoesNotContain("test-only-token", Options.ToString());
    }
    [Fact]
    public async Task DownloadsValidWadAndVerifiesGitBlobHash()
    {
        var wad = Wad(); var prefix = Encoding.ASCII.GetBytes("blob 305\0");
        var sha = Convert.ToHexString(SHA1.HashData(prefix.Concat(wad).ToArray()));
        using var repo = Repo(request =>
        {
            Assert.Contains("/raw/", request.RequestUri!.AbsolutePath);
            Assert.Contains("暗夜猎手-薇恩", Uri.UnescapeDataString(request.RequestUri.AbsolutePath));
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(wad) };
        });
        var file = await repo.DownloadAsync(Options, Entry(sha));
        Assert.Equal("源计划：净化.wad.client", Path.GetFileName(file)); Assert.Equal(wad, File.ReadAllBytes(file));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptOrWrongHashDownloadsNeverBecomePackages(bool corrupt)
    {
        using var repo = Repo(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(corrupt ? new byte[305] : Wad()) });
        await Assert.ThrowsAsync<InvalidDataException>(() => repo.DownloadAsync(Options, Entry(new string('0', 40))));
        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }
    [Fact]
    public async Task AuthFailureHasActionableMessageAndDoesNotExposeToken()
    {
        using var repo = Repo(_ => new(HttpStatusCode.NotFound));
        var error = await Assert.ThrowsAsync<IOException>(() => repo.ListAsync(Options, ""));
        Assert.Contains("权限", error.Message); Assert.DoesNotContain("test-only-token", error.ToString());
    }
    [Fact]
    public async Task RedirectedDownloadsDoNotForwardAccessToken()
    {
        int calls = 0;
        using var repo = Repo(request =>
        {
            if (calls++ == 0)
            {
                Assert.NotNull(request.Headers.Authorization);
                var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new Uri("https://download.gitee.com/resource.wad.client"); return response;
            }
            Assert.Null(request.Headers.Authorization);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Wad()) };
        });
        Assert.True(File.Exists(await repo.DownloadAsync(Options, Entry()))); Assert.Equal(2, calls);
    }
    [Fact]
    public async Task InvalidDirectoryPathsAndOversizeDownloadsAreRejected()
    {
        using var repo = Repo(_ => Json(new[] { new { name = "escape.wad.client", path = "../escape.wad.client", type = "file", size = 305 } }));
        await Assert.ThrowsAsync<InvalidDataException>(() => repo.ListAsync(Options, ""));
        await Assert.ThrowsAsync<InvalidDataException>(() => repo.DownloadAsync(Options, Entry() with { Size = 3L * 1024 * 1024 * 1024 }));
        Assert.False(Directory.Exists(_root));
    }
    [Fact]
    public async Task HttpRedirectIsRejectedWithoutSendingCredentials()
    {
        int calls = 0;
        using var repo = Repo(_ =>
        {
            calls++; var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("http://download.example.test/file.wad.client"); return response;
        });
        await Assert.ThrowsAsync<IOException>(() => repo.DownloadAsync(Options, Entry())); Assert.Equal(1, calls);
        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }
    [Fact]
    public async Task CancellationRemovesPartialDownload()
    {
        using var cancel = new CancellationTokenSource();
        using var repo = Repo(_ => new(HttpStatusCode.OK) { Content = new StreamContent(new CancelAfterReadStream(Wad(), cancel)) });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repo.DownloadAsync(Options, Entry(), token: cancel.Token));
        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }
    [Fact]
    public async Task InstallsPinnedLoaderAndPreservesOtherVersions()
    {
        string game = Path.Combine(_root, "Game"); Directory.CreateDirectory(game); File.WriteAllText(Path.Combine(game, "League of Legends.exe"), "fixture");
        string source = Path.Combine(AppContext.BaseDirectory, "Tools", "ResourceSkins", "HID.dll"); Assert.True(File.Exists(source));
        var loader = new ResourceSkinLoader(source, () => false); await loader.EnsureInstalledAsync(game);
        string target = Path.Combine(game, "HID.dll");
        Assert.Equal(ResourceSkinLoader.Sha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))));
        await new ResourceSkinLoader("missing-source", () => false).EnsureInstalledAsync(game);
        File.WriteAllText(target, "another-loader");
        await Assert.ThrowsAsync<IOException>(() => loader.EnsureInstalledAsync(game)); Assert.Equal("another-loader", File.ReadAllText(target));
    }
    [Fact]
    public async Task FileSkinModeDisablesKnownLoaderAndResourceModeRestoresIt()
    {
        string game = Path.Combine(_root, "Game"); Directory.CreateDirectory(game); File.WriteAllText(Path.Combine(game, "League of Legends.exe"), "fixture");
        string source = Path.Combine(AppContext.BaseDirectory, "Tools", "ResourceSkins", "HID.dll"); Assert.True(File.Exists(source));
        var loader = new ResourceSkinLoader(source, () => false);
        string target = Path.Combine(game, "HID.dll"), backup = Path.Combine(game, GameSkinLoaderIsolation.DisabledLoaderName);
        for (int i = 0; i < 2; i++)
        {
            await loader.EnsureInstalledAsync(game);
            await loader.DisableAsync(game);
            Assert.False(File.Exists(target)); Assert.True(File.Exists(backup));
            Assert.True(await GameSkinLoaderIsolation.IsKnownLoaderAsync(backup, default));
            await loader.DisableAsync(game);
            await new ResourceSkinLoader("missing-source", () => false).EnsureInstalledAsync(game);
            Assert.True(File.Exists(target)); Assert.False(File.Exists(backup));
        }
    }

    [Fact]
    public async Task LoaderIsolationPreservesRunningGameAndExistingBackup()
    {
        string game = Path.Combine(_root, "Game"); Directory.CreateDirectory(game); File.WriteAllText(Path.Combine(game, "League of Legends.exe"), "fixture");
        string source = Path.Combine(AppContext.BaseDirectory, "Tools", "ResourceSkins", "HID.dll");
        await new ResourceSkinLoader(source, () => false).EnsureInstalledAsync(game);
        string target = Path.Combine(game, "HID.dll"), backup = Path.Combine(game, GameSkinLoaderIsolation.DisabledLoaderName);
        await Assert.ThrowsAsync<InvalidOperationException>(() => GameSkinLoaderIsolation.DisableResourceLoaderAsync(game, () => true, default));
        Assert.True(File.Exists(target)); Assert.False(File.Exists(backup));
        File.WriteAllText(backup, "previous-backup");
        await Assert.ThrowsAsync<IOException>(() => GameSkinLoaderIsolation.DisableResourceLoaderAsync(game, () => false, default));
        Assert.True(File.Exists(target)); Assert.Equal("previous-backup", File.ReadAllText(backup));
        File.Delete(target);
        await Assert.ThrowsAsync<IOException>(() => new ResourceSkinLoader(source, () => false).EnsureInstalledAsync(game));
        Assert.False(File.Exists(target)); Assert.Equal("previous-backup", File.ReadAllText(backup));
    }

    [Fact]
    public async Task BadLoaderAndRunningGameDoNotInstall()
    {
        string game = Path.Combine(_root, "Game"); Directory.CreateDirectory(game); File.WriteAllText(Path.Combine(game, "League of Legends.exe"), "fixture");
        string bad = Path.Combine(_root, "bad.dll"); File.WriteAllText(bad, "bad source");
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ResourceSkinLoader(bad, () => true).EnsureInstalledAsync(game));
        await Assert.ThrowsAsync<InvalidDataException>(() => new ResourceSkinLoader(bad, () => false).EnsureInstalledAsync(game));
        Assert.Equal([Path.Combine(game, "League of Legends.exe")], Directory.GetFiles(game));
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); return Task.FromResult(action(request));
        }
    }
    private sealed class CancelAfterReadStream(byte[] bytes, CancellationTokenSource cancellation) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            int read = await base.ReadAsync(buffer[..Math.Min(buffer.Length, 100)], token);
            cancellation.Cancel(); return read;
        }
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
