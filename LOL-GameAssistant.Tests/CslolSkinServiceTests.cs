using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Infrastructure.Skins;
using LOL_GameAssistant.Domain.Skins;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class CslolSkinServiceTests
{
    [Fact]
    public async Task OverlayKeepsStdinOpenAndDisposesItsOwnProcess()
    {
        if (!OperatingSystem.IsWindows()) return;
        var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var runner = new CslolToolRunner(shell);
        var session = await runner.StartOverlayAsync(["-NoProfile", "-Command", "Write-Output 'Waiting'; [Console]::ReadLine() | Out-Null"], _ => { }, default);
        Assert.True(session.IsRunning);
        int pid = session.Pid; session.Dispose();
        Assert.False(session.IsRunning);
        Assert.Throws<ArgumentException>(() => System.Diagnostics.Process.GetProcessById(pid));
    }

    [Fact]
    public async Task OverlayEarlyExitIsReportedInsteadOfSuccessfulApplication()
    {
        if (!OperatingSystem.IsWindows()) return;
        var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var runner = new CslolToolRunner(shell);
        var error = await Assert.ThrowsAsync<IOException>(() => runner.StartOverlayAsync(["-NoProfile", "-Command", "Write-Output 'startup-failure'; exit 5"], _ => { }, default));
        Assert.Contains("startup-failure", error.Message);
    }
    [Fact]
    public async Task AppliesVerifiedPackagesWithArgumentBoundariesAndStopsOnlyOwnedOverlay()
    {
        using var fixture = new Fixture(); using var service = fixture.Service();
        var catalog = await service.LoadCatalogAsync();
        Assert.Equal("Annie", Assert.Single(catalog).ChampionKey);
        var result = await service.ApplyAsync(fixture.Game, catalog);
        Assert.True(result.Ok, result.Message); Assert.Equal("waiting", result.Stage);
        Assert.True(service.IsRunning); Assert.Equal(3, fixture.Runner.Calls.Count);
        Assert.Contains("--game:" + fixture.Game, fixture.Runner.Calls[0]);
        Assert.Equal(fixture.Entry.Size, new FileInfo(fixture.Runner.Calls[0][1]).Length);
        Assert.True((await service.StopAsync()).Ok); Assert.False(service.IsRunning);
        Assert.True(fixture.Runner.Session.Disposed);
    }

    [Fact]
    public async Task ForeignResourceLoaderRejectsBeforeToolsAndIsPreserved()
    {
        using var fixture = new Fixture(); using var service = fixture.Service();
        var loader = Path.Combine(fixture.Game, "HID.dll");
        await File.WriteAllTextAsync(loader, "foreign-loader");
        var result = await service.ApplyAsync(fixture.Game, await service.LoadCatalogAsync());
        Assert.Equal("loader-conflict", result.Stage); Assert.Empty(fixture.Runner.Calls);
        Assert.Equal("foreign-loader", await File.ReadAllTextAsync(loader));
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task RunningGameRejectsBeforeAnyToolAndDoesNotStopExistingOverlay()
    {
        using var fixture = new Fixture(); bool running = false;
        using var service = fixture.Service(game: () => running);
        var entries = await service.LoadCatalogAsync(); Assert.True((await service.ApplyAsync(fixture.Game, entries)).Ok);
        int count = fixture.Runner.Calls.Count; running = true;
        Assert.Equal("game-running", (await service.ApplyAsync(fixture.Game, entries)).Stage);
        Assert.Equal(count, fixture.Runner.Calls.Count); Assert.False(fixture.Runner.Session.Disposed);
    }

    [Fact]
    public async Task MemoryEngineAndForeignEntriesRejectWithoutStartingTools()
    {
        using var fixture = new Fixture(); bool memory = true; using var service = fixture.Service(memory: () => memory);
        var entries = await service.LoadCatalogAsync();
        Assert.Equal("engine-conflict", (await service.ApplyAsync(fixture.Game, entries)).Stage);
        memory = false;
        Assert.Equal("catalog", (await service.ApplyAsync(fixture.Game, [fixture.Entry with { FileName = "other" }])).Stage);
        Assert.Empty(fixture.Runner.Calls);
    }

    [Fact]
    public async Task SameChampionIncludingChromasConflictsBeforeImport()
    {
        using var fixture = new Fixture();
        var chroma = fixture.Entry with { ChromaId = "2", FileName = "other.fantome" };
        await File.WriteAllTextAsync(fixture.Manifest, JsonSerializer.Serialize(new { files = new[] { fixture.Entry, chroma } }));
        using var service = fixture.Service(); var catalog = await service.LoadCatalogAsync();
        Assert.Equal("conflict", (await service.ApplyAsync(fixture.Game, catalog)).Stage);
        Assert.Equal(catalog[0].ConflictKey, catalog[1].ConflictKey); Assert.Empty(fixture.Runner.Calls);
    }

    [Fact]
    public async Task CorruptedSameLengthCacheIsReplacedUsingHash()
    {
        using var fixture = new Fixture();
        var cache = Path.Combine(fixture.Work, "package-cache", fixture.Entry.Sha256 + ".fantome");
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        await File.WriteAllBytesAsync(cache, new byte[fixture.Entry.Size]);
        using var service = fixture.Service(); var entries = await service.LoadCatalogAsync();
        Assert.True((await service.ApplyAsync(fixture.Game, entries)).Ok);
        Assert.Equal(fixture.Plain, await File.ReadAllBytesAsync(cache));
    }

    [Fact]
    public async Task StopCancelsImportAndPreventsLaterOverlayStart()
    {
        using var fixture = new Fixture(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Runner.Import = async token => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); };
        using var service = fixture.Service(); var entries = await service.LoadCatalogAsync();
        var apply = service.ApplyAsync(fixture.Game, entries); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stopped = await service.StopAsync(); var result = await apply;
        Assert.True(stopped.Ok); Assert.Equal("cancelled", result.Stage); Assert.False(service.IsRunning);
        Assert.Single(fixture.Runner.Calls);
    }

    [Fact]
    public async Task GameStartingDuringPreparationPreventsOverlay()
    {
        using var fixture = new Fixture(); bool running = false;
        fixture.Runner.Import = _ => { running = true; return Task.CompletedTask; };
        using var service = fixture.Service(game: () => running); var entries = await service.LoadCatalogAsync();
        Assert.Equal("game-running", (await service.ApplyAsync(fixture.Game, entries)).Stage);
        Assert.Single(fixture.Runner.Calls); Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task LocalLibraryTakesPriorityAndVerifiesHashWithoutReadingPackage()
    {
        using var fixture = new Fixture(); var library = Path.Combine(fixture.Root, "skinlib");
        var source = Path.Combine(library, "Annie", "Goth Annie", "Goth Annie.fantome");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!); await File.WriteAllBytesAsync(source, fixture.Plain);
        await File.WriteAllTextAsync(fixture.Package, "invalid encrypted package");
        using var service = fixture.Service(library: library); var entries = await service.LoadCatalogAsync();
        Assert.True((await service.ApplyAsync(fixture.Game, entries)).Ok);
        Assert.Equal(source, fixture.Runner.Calls[0][1]);
    }

    [Theory]
    [InlineData(-1, 20)]
    [InlineData(10000, 20)]
    [InlineData(0, -1)]
    public void PackageRejectsInvalidEntryRanges(long offset, int size)
    {
        using var fixture = new Fixture(); fixture.WritePackage(offset, size);
        using var store = new SkinPackageStore(fixture.Package, fixture.Key);
        Assert.Throws<InvalidDataException>(() => store.Find(fixture.Entry.FileName));
    }

    [Fact]
    public async Task WrongAuthenticationTagDoesNotPublishPartialFantome()
    {
        using var fixture = new Fixture(); var bytes = await File.ReadAllBytesAsync(fixture.Package); bytes[12] ^= 1;
        await File.WriteAllBytesAsync(fixture.Package, bytes);
        using var service = fixture.Service(); var entries = await service.LoadCatalogAsync();
        Assert.False((await service.ApplyAsync(fixture.Game, entries)).Ok);
        Assert.Empty(fixture.Runner.Calls);
        Assert.Empty(Directory.EnumerateFiles(fixture.Work, "*.fantome", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task NativeCoreCannotEnableWhenFileEngineOwnsSession()
    {
        var service = new NativeSkinCoreService(new NeverTransport(), memoryAllowed: () => false);
        Assert.Throws<SkinCoreException>(() => service.SetEnabled(true));
        await Assert.ThrowsAsync<SkinCoreException>(() => service.GetCatalogAsync());
    }
    private sealed class NeverTransport : ISkinCoreTransport
    { public Task<SkinCoreReply> InvokeAsync(IReadOnlyList<string> args, CancellationToken token) => throw new InvalidOperationException("Transport must not run"); }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "cslol-tests-" + Guid.NewGuid().ToString("N"));
        public string Game => Path.Combine(Root, "game with spaces");
        public string Work => Path.Combine(Root, "work");
        public string Manifest => Path.Combine(Root, "manifest.json");
        public string Package => Path.Combine(Root, "skins.plskin");
        public string Key { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        public byte[] Plain { get; } = Encoding.UTF8.GetBytes("synthetic fantome fixture");
        public CslolSkinEntry Entry { get; }
        public Runner Runner { get; } = new();
        public Fixture()
        {
            Directory.CreateDirectory(Game); Directory.CreateDirectory(Path.Combine(Game, "DATA"));
            File.WriteAllText(Path.Combine(Game, "League of Legends.exe"), "fixture");
            Directory.CreateDirectory(Path.Combine(Root, "tools"));
            foreach (var name in new[] { "mod-tools.exe", "cslol-dll.dll", "hashes.game.txt" }) File.WriteAllText(Path.Combine(Root, "tools", name), "fixture");
            Entry = new() { ChampionKey = "Annie", SkinName = "Goth Annie", SkinId = 1001, SkinNum = 1, FileName = "skins/Annie/Goth Annie.fantome",
                Size = Plain.Length, Sha256 = Convert.ToHexString(SHA256.HashData(Plain)).ToLowerInvariant() };
            File.WriteAllText(Manifest, JsonSerializer.Serialize(new { files = new[] { Entry } })); WritePackage();
        }
        public void WritePackage(long offset = 0, int? size = null)
        {
            var nonce = RandomNumberGenerator.GetBytes(12); var cipher = new byte[Plain.Length]; var tag = new byte[16];
            using (var aes = new AesGcm(Convert.FromBase64String(Key), 16)) aes.Encrypt(nonce, Plain, cipher, tag);
            using var stream = File.Create(Package); stream.Write(Encoding.ASCII.GetBytes("PLSKINPK")); stream.Write(BitConverter.GetBytes((uint)cipher.Length)); stream.Write(cipher);
            stream.Write(JsonSerializer.SerializeToUtf8Bytes(new { files = new[] { new { Entry.FileName, offset, encryptedSize = size ?? cipher.Length, size = size ?? Plain.Length,
                nonce = Convert.ToBase64String(nonce), tag = Convert.ToBase64String(tag) } } }));
        }
        public CslolSkinService Service(Func<bool>? memory = null, Func<bool>? game = null, string? library = null) =>
            new(Path.Combine(Root, "tools"), Package, Manifest, Work, Key, library, memory, game ?? (() => false), Runner);
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
    private sealed class Runner : ICslolToolRunner
    {
        public List<string[]> Calls { get; } = [];
        public Session Session { get; } = new();
        public Func<CancellationToken, Task>? Import;
        public async Task<CslolToolResult> RunAsync(IReadOnlyList<string> args, Action<string> status, CancellationToken token)
        {
            Calls.Add(args.ToArray());
            if (args[0] == "import")
            {
                if (Import != null) await Import(token);
                Directory.CreateDirectory(Path.Combine(args[2], "WAD"));
                File.WriteAllText(Path.Combine(args[2], "WAD", "Annie.wad.client"), "fixture");
            }
            return new(true, "");
        }
        public Task<ICslolOverlaySession> StartOverlayAsync(IReadOnlyList<string> args, Action<string> status, CancellationToken token)
        { Calls.Add(args.ToArray()); return Task.FromResult<ICslolOverlaySession>(Session); }
    }
    private sealed class Session : ICslolOverlaySession
    {
        public bool Disposed; public bool IsRunning => !Disposed; public int Pid => 123;
        public void Dispose() => Disposed = true;
    }
}
