using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LOL_GameAssistant.Domain.ApplicationInfo;
using LOL_GameAssistant.Infrastructure.ApplicationInfo;
using LOL_GameAssistant.BaseViewForm;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class UpdatePackageServiceTests
{
    [Fact]
    public async Task CompletedDownloadIsRestoredByNewServiceWithoutAnyNetworkRequests()
    {
        using var fixture = new PackageFixture();
        var first = await fixture.Service.PrepareAsync(fixture.Release, null, default);
        File.WriteAllText(first.JobPath, "stale process identity", Encoding.Unicode);
        var restored = await fixture.CreateService().TryRestoreAsync(fixture.Release, null, default);
        Assert.Equal(first, restored);
        Assert.Equal(2, fixture.Requests.Count);
        Assert.Contains("ParentPid=" + Environment.ProcessId, File.ReadAllText(first.JobPath, Encoding.Unicode));
        var reused = await fixture.CreateService().PrepareAsync(fixture.Release, null, default, useServer: true);
        Assert.Equal(first, reused);
        Assert.Equal(2, fixture.Requests.Count);
        Assert.Equal("existing app", File.ReadAllText(Path.Combine(fixture.Install, "sc助手.exe")));
    }

    [Theory]
    [InlineData("package.zip")]
    [InlineData("stage/sc助手.exe")]
    [InlineData("manifest.json")]
    public async Task DamagedCacheIsRejectedAndCanBeDownloadedAgain(string damagedFile)
    {
        using var fixture = new PackageFixture();
        var first = await fixture.Service.PrepareAsync(fixture.Release, null, default);
        File.WriteAllText(Path.Combine(first.Directory, damagedFile), "damaged");
        Assert.Null(await fixture.CreateService().TryRestoreAsync(fixture.Release, null, default));
        var replacement = await fixture.CreateService().PrepareAsync(fixture.Release, null, default);
        Assert.NotEqual(first.Token, replacement.Token);
        Assert.Equal(4, fixture.Requests.Count);
    }

    [Fact]
    public async Task CacheDoesNotReuseAnotherVersionOrStartedInstallationAndHonorsCancellation()
    {
        using var fixture = new PackageFixture();
        var ready = await fixture.Service.PrepareAsync(fixture.Release, null, default);
        Assert.Null(await fixture.Service.TryRestoreAsync(fixture.Release with { Version = new Version(99, 9, 9) }, null, default));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.TryRestoreAsync(fixture.Release, null, cancellation.Token));
        File.WriteAllText(Path.Combine(ready.Directory, "state.txt"), "installing");
        Assert.Null(await fixture.Service.TryRestoreAsync(fixture.Release, null, default));
        File.WriteAllText(Path.Combine(fixture.Install, ".update", "active-job.txt"), ready.Token);
        Assert.Null(await fixture.Service.TryRestoreAsync(fixture.Release, null, default));
        await Assert.ThrowsAsync<IOException>(() => fixture.Service.PrepareAsync(fixture.Release, null, default));
        Assert.Equal(2, fixture.Requests.Count);
    }

    [Fact]
    public async Task ReopenedPromptOffersInstallForCompletedDownload()
    {
        using var fixture = new PackageFixture();
        await fixture.Service.PrepareAsync(fixture.Release, null, default);
        MatchListScrollingTests.OnUiThread(() =>
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                using var prompt = new UpdateAvailableForm(fixture.Release, packageService: fixture.CreateService());
                prompt.Show();
                var button = (AntdUI.Button)prompt.Controls.Find("DownloadAndInstallUpdate", true).Single();
                var timer = Stopwatch.StartNew();
                while (!button.Enabled && timer.Elapsed < TimeSpan.FromSeconds(5))
                {
                    System.Windows.Forms.Application.DoEvents();
                    Thread.Sleep(10);
                }
                Assert.True(button.Enabled);
                Assert.Equal("重启并安装", button.Text);
                Assert.False(prompt.Controls.Find("UpdateDownloadSource", true).Single().Enabled);
                Assert.Contains("无需重复下载", prompt.Controls.Find("UpdateDownloadStatus", true).Single().Text);
                prompt.Close();
            }
        });
        Assert.Equal(2, fixture.Requests.Count);
    }

    [Theory]
    [InlineData("../outside.exe")]
    [InlineData("/absolute.exe")]
    [InlineData("C:/outside.exe")]
    [InlineData("a/../b.exe")]
    [InlineData("a\\b.exe")]
    [InlineData(".update/job.ini")]
    [InlineData("settings.json")]
    [InlineData("logs/file.log")]
    [InlineData("a/CON.txt")]
    [InlineData("a.exe:ads")]
    public void RejectsPathsOutsideManagedProgramFiles(string path) =>
        Assert.Throws<InvalidDataException>(() => UpdatePackageService.ValidateRelativePath(path));

    [Fact]
    public async Task DownloadsVerifiesAndPreparesTaskWithoutChangingInstalledFiles()
    {
        using var fixture = new PackageFixture();
        var progress = new List<UpdateDownloadProgress>();
        var result = await fixture.Service.PrepareAsync(fixture.Release, new InlineProgress(progress.Add), default);
        Assert.True(File.Exists(result.JobPath));
        Assert.Equal("existing app", File.ReadAllText(Path.Combine(fixture.Install, "sc助手.exe")));
        Assert.Equal("user settings", File.ReadAllText(Path.Combine(fixture.Install, "settings.json")));
        Assert.Equal(fixture.Config.Version, result.Version);
        Assert.Equal("native updater placeholder", File.ReadAllText(Path.Combine(result.Directory, "runner", "Updater.exe")));
        Assert.True(File.Exists(Path.Combine(result.Directory, "stage", "sc助手.exe")));
        Assert.False(File.Exists(Path.Combine(fixture.Install, ".update", "active-job.txt")));
        Assert.Equal(2, fixture.Requests.Count);
        Assert.All(fixture.Requests, uri =>
        {
            Assert.Equal("github.com", uri.Host);
            Assert.Contains("/download/" + fixture.Release.TagName + "/", uri.AbsoluteUri);
            Assert.DoesNotContain("latest", uri.AbsoluteUri);
        });
        Assert.Contains(progress, value => value.Percent == 100);
        string job = File.ReadAllText(result.JobPath, Encoding.Unicode);
        Assert.Contains("ParentPid=" + Environment.ProcessId, job);
        Assert.Contains("Token=" + result.Token, job);
    }

    [Fact]
    public async Task X64GithubCanStillUseLegacyManifestAttachment()
    {
        if (UpdatePlatform.Current != "win-x64") return;
        using var fixture = new PackageFixture();
        fixture.LegacyManifestOnly = true;
        var result = await fixture.Service.PrepareAsync(fixture.Release, null, default);
        Assert.True(File.Exists(result.JobPath));
        Assert.Equal(3, fixture.Requests.Count);
        Assert.EndsWith("/update-win-x64.json", fixture.Requests[0].AbsolutePath);
        Assert.EndsWith("/update.json", fixture.Requests[1].AbsolutePath);
    }

    [Fact]
    public async Task ServerSourceDownloadsVersionedManifestAndPackageAndVerifiesThem()
    {
        using var fixture = new PackageFixture();
        var release = fixture.Release with { ServerManifestUrl = new Uri("https://updates.example.com/app/v" + fixture.Release.Version + "/" + UpdatePlatform.Current + "/update.json") };
        var result = await fixture.Service.PrepareAsync(release, null, default, useServer: true);
        Assert.True(File.Exists(result.JobPath));
        Assert.Equal(2, fixture.Requests.Count);
        Assert.All(fixture.Requests, uri =>
        {
            Assert.Equal("updates.example.com", uri.Host);
            Assert.StartsWith("/app/v" + fixture.Release.Version + "/" + UpdatePlatform.Current + "/", uri.AbsolutePath);
        });
        Assert.Equal("existing app", File.ReadAllText(Path.Combine(fixture.Install, "sc助手.exe")));
    }

    [Fact]
    public async Task ServerSourceRejectsTamperedPackageAndAllowsRetryUsingGithub()
    {
        using var fixture = new PackageFixture();
        var release = fixture.Release with { ServerManifestUrl = new Uri("https://updates.example.com/app/v" + fixture.Release.Version + "/" + UpdatePlatform.Current + "/update.json") };
        string hash = fixture.Config.Sha256;
        fixture.Config = fixture.Config with { Sha256 = new string('0', 64) };
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.PrepareAsync(release, null, default, useServer: true));
        Assert.Empty(Directory.GetDirectories(Path.Combine(fixture.Install, ".update")));
        fixture.Config = fixture.Config with { Sha256 = hash };
        var result = await fixture.Service.PrepareAsync(release, null, default);
        Assert.True(File.Exists(result.JobPath));
        Assert.All(fixture.Requests.TakeLast(2), uri => Assert.Equal("github.com", uri.Host));
    }

    [Fact]
    public async Task WrongPackageHashLeavesInstalledApplicationUntouchedAndRemovesPartialDownload()
    {
        using var fixture = new PackageFixture();
        fixture.Config = fixture.Config with { Sha256 = new string('0', 64) };
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.PrepareAsync(fixture.Release, null, default));
        Assert.Equal("existing app", File.ReadAllText(Path.Combine(fixture.Install, "sc助手.exe")));
        Assert.Empty(Directory.GetDirectories(Path.Combine(fixture.Install, ".update")));
    }

    [Fact]
    public async Task CancellationRemovesPreparedFilesWithoutChangingCurrentVersion()
    {
        using var fixture = new PackageFixture();
        using var cancellation = new CancellationTokenSource();
        fixture.OnPackageRequest = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.PrepareAsync(fixture.Release, null, cancellation.Token));
        Assert.Equal("existing app", File.ReadAllText(Path.Combine(fixture.Install, "sc助手.exe")));
        Assert.Empty(Directory.GetDirectories(Path.Combine(fixture.Install, ".update")));
    }

    [Fact]
    public async Task MissingReleaseConfigurationFailsBeforeCreatingTransaction()
    {
        using var fixture = new PackageFixture();
        fixture.ConfigStatus = HttpStatusCode.NotFound;
        await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Service.PrepareAsync(fixture.Release, null, default));
        Assert.False(Directory.Exists(Path.Combine(fixture.Install, ".update")));
    }

    [Fact]
    public void RejectsWrongVersionPlatformAndExecutable()
    {
        using var fixture = new PackageFixture();
        foreach (var config in new[]
        {
            fixture.Config with { Version = "0.0.1" },
            fixture.Config with { Runtime = "linux-x64" },
            fixture.Config with { EntryExecutable = "other.exe" },
            fixture.Config with { Sha256 = "invalid" }
        })
            Assert.Throws<InvalidDataException>(() => UpdatePackageService.ValidateManifest(config, fixture.Release));
    }

    [Theory]
    [InlineData("../escape.exe")]
    [InlineData("settings.json")]
    [InlineData(".update/job.ini")]
    public void UnsafeZipEntryIsRejectedBeforeWritingOutsideStage(string name)
    {
        using var fixture = new PackageFixture();
        string zip = Path.Combine(fixture.Root, "unsafe.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry(name).Open())) writer.Write("unsafe");
        Assert.Throws<InvalidDataException>(() => UpdatePackageService.ExtractAndVerify(zip, Path.Combine(fixture.Root, "stage"), default));
        Assert.False(File.Exists(Path.Combine(fixture.Root, "escape.exe")));
    }

    [Fact]
    public void TamperedOrUnlistedZipFileIsRejected()
    {
        using var fixture = new PackageFixture();
        string zip = Path.Combine(fixture.Root, "tampered.zip");
        File.WriteAllBytes(zip, fixture.Package);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        using (var writer = new StreamWriter(archive.CreateEntry("unlisted.dll").Open())) writer.Write("unexpected");
        Assert.Throws<InvalidDataException>(() => UpdatePackageService.ExtractAndVerify(zip, Path.Combine(fixture.Root, "stage"), default));
    }

    private sealed class InlineProgress(Action<UpdateDownloadProgress> report) : IProgress<UpdateDownloadProgress>
    {
        public void Report(UpdateDownloadProgress value) => report(value);
    }

    private sealed class PackageFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "LOL Update 下载测试 " + Guid.NewGuid().ToString("N"));
        public string Install => Path.Combine(Root, "install");
        public byte[] Package { get; }
        public UpdateManifest Config { get; set; }
        public UpdateRelease Release { get; }
        public UpdatePackageService Service { get; }
        public List<Uri> Requests { get; } = [];
        public HttpStatusCode ConfigStatus { get; set; } = HttpStatusCode.OK;
        public bool LegacyManifestOnly { get; set; }
        public Action? OnPackageRequest { get; set; }
        private readonly HttpClient _client;
        public UpdatePackageService CreateService() => new(_client, Install);

        public PackageFixture()
        {
            Directory.CreateDirectory(Install);
            File.WriteAllText(Path.Combine(Install, "sc助手.exe"), "existing app");
            File.WriteAllText(Path.Combine(Install, "Updater.exe"), "native updater placeholder");
            File.WriteAllText(Path.Combine(Install, "settings.json"), "user settings");
            // 复制真实 PE 并只修改副本的固定版本资源。测试不能假设 testhost 版本高于应用版本。
            byte[] executable = File.ReadAllBytes(Environment.ProcessPath!);
            var version = new Version(AppVersion.Current.Major + 1, 0, 0, 0);
            byte[] resourceKey = Encoding.Unicode.GetBytes("VS_VERSION_INFO\0");
            int keyOffset = executable.AsSpan().IndexOf(resourceKey);
            Assert.True(keyOffset >= 0);
            int fixedInfo = (keyOffset + resourceKey.Length + 3) & ~3;
            Assert.Equal(0xFEEF04BDu, BitConverter.ToUInt32(executable, fixedInfo));
            uint majorMinor = checked((uint)version.Major << 16);
            BitConverter.GetBytes(majorMinor).CopyTo(executable, fixedInfo + 8);
            BitConverter.GetBytes(0u).CopyTo(executable, fixedInfo + 12);
            string fixturePath = Path.Combine(Root, "future-app.exe");
            File.WriteAllBytes(fixturePath, executable);
            var info = FileVersionInfo.GetVersionInfo(fixturePath);
            Assert.Equal(version, new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart));
            Assert.True(AppVersion.IsNewer(version, AppVersion.Current));
            Release = new(version, "v" + version, "https://github.com/shecong/LOL-GameAssistant/releases/tag/v" + version, "");
            using var memory = new MemoryStream();
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
            {
                Add(zip, "sc助手.exe", executable);
                byte[] updater = Encoding.UTF8.GetBytes("new updater");
                Add(zip, "Updater.exe", updater);
                string list = Convert.ToHexString(SHA256.HashData(executable)) + "\tsc助手.exe\n"
                    + Convert.ToHexString(SHA256.HashData(updater)) + "\tUpdater.exe\n";
                Add(zip, "update-files.txt", Encoding.UTF8.GetBytes(list));
            }
            Package = memory.ToArray();
            Config = new(1, version.ToString(), "win-x64", "LOL-GameAssistant-win-x64.zip", Package.Length,
                Convert.ToHexString(SHA256.HashData(Package)), "sc助手.exe");
            _client = new HttpClient(new StubHandler(request =>
            {
                Requests.Add(request.RequestUri!);
                if (LegacyManifestOnly && request.RequestUri!.AbsolutePath.EndsWith("/" + UpdatePlatform.GithubManifest))
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                if (request.RequestUri!.AbsolutePath.EndsWith(".json"))
                    return new HttpResponseMessage(ConfigStatus) { Content = new StringContent(JsonSerializer.Serialize(Config)) };
                OnPackageRequest?.Invoke();
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Package) };
            }));
            Service = new UpdatePackageService(_client, Install);
        }
        private static void Add(ZipArchive zip, string name, byte[] bytes)
        {
            using var stream = zip.CreateEntry(name).Open(); stream.Write(bytes);
        }
        public void Dispose() { _client.Dispose(); Directory.Delete(Root, true); }
    }
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
