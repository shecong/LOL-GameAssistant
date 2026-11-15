using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using LOL_GameAssistant.Infrastructure.Pengu;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class PenguReleaseInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sc-gitee-tests", Guid.NewGuid().ToString("N"));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static byte[] Package(bool traversal = false)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var (name, text) in new[] {
                ("PenguLoader/bin/core.dll", "MZfixture"),
                ("PenguLoader/loader/src-tauri/tauri.conf.json", "{\"package\":{\"version\":\"1.2.0\"}}"),
                ("PenguLoader/extensions/lol-game-assistant-visuals/index.js", "// fixture") })
            { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(text); }
            if (traversal) { using var writer = new StreamWriter(zip.CreateEntry("../escaped.txt").Open()); writer.Write("invalid"); }
        }
        return output.ToArray();
    }
    [Fact]
    public async Task DownloadsPinnedGiteePackageAndReusesItWithoutLosingSettings()
    {
        byte[] zip = Package();
        var handler = new Handler(zip);
        using var http = new HttpClient(handler);
        var installer = new PenguReleaseInstaller(_root, http, Hash(zip), Hash(Encoding.UTF8.GetBytes("MZfixture")));
        string directory = await installer.InstallAsync();
        Assert.Equal(PenguReleaseInstaller.DownloadUrl, handler.Url);
        Assert.Equal("MZfixture", File.ReadAllText(Path.Combine(directory, "bin", "core.dll")));
        string settings = Path.Combine(directory, "bin", "config");
        File.WriteAllText(settings, "user config");
        Assert.Equal(directory, await installer.InstallAsync());
        Assert.Equal("user config", File.ReadAllText(settings));
        Assert.Equal(1, handler.Calls);
        File.WriteAllText(Path.Combine(directory, "bin", "core.dll"), "damaged");
        await Assert.ThrowsAsync<IOException>(() => installer.InstallAsync());
    }
    [Fact]
    public async Task RejectsChangedDownloadBeforeExtractingAnything()
    {
        using var http = new HttpClient(new Handler(Encoding.UTF8.GetBytes("<html>login</html>")));
        var installer = new PenguReleaseInstaller(_root, http, Hash(Package()));
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync());
        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }
    [Fact]
    public async Task RejectsTraversalAndCleansStagingDirectory()
    {
        byte[] zip = Package(traversal: true);
        using var http = new HttpClient(new Handler(zip));
        await Assert.ThrowsAsync<IOException>(() => new PenguReleaseInstaller(_root, http, Hash(zip)).InstallAsync());
        Assert.False(File.Exists(Path.Combine(_root, "escaped.txt")));
        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }
    [Theory]
    [InlineData("[app]\nlow_spec_mode = 1\n[client]\nplugins_dir = unrelated")]
    [InlineData("[app]\nplugins_dir = old\nlow_spec_mode = 1\n[client]\nplugins_dir = unrelated")]
    public void SwitchingCorePreservesUserOptionsAndExistingPluginDirectory(string config)
    {
        Directory.CreateDirectory(_root);
        string file = Path.Combine(_root, "config");
        string plugins = Path.Combine(_root, "existing plugins");
        PenguLocalLoaderService.PreserveConfiguration(file, config, plugins);
        string actual = File.ReadAllText(file);
        Assert.Contains("low_spec_mode = 1", actual);
        Assert.Contains("plugins_dir = " + plugins, actual);
        Assert.Contains("[client]" + Environment.NewLine + "plugins_dir = unrelated", actual);
        Assert.DoesNotContain("plugins_dir = old", actual);
    }
    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
    private sealed class Handler(byte[] bytes) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Url { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++; Url = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }
}
