using System.Buffers.Binary;
using System.IO.Compression;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Domain.Skins;
using LOL_GameAssistant.Infrastructure.Skins;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class ResourceSkinTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "resource-skin-test-" + Guid.NewGuid().ToString("N"));
    private string Game => Path.Combine(_root, "Game");
    private string Journal => Path.Combine(_root, "state", "mounts.json");
    private string Mount => Path.Combine(Game, "DATA", "FINAL", "jinju2");
    public ResourceSkinTests()
    {
        Directory.CreateDirectory(Game); File.WriteAllText(Path.Combine(Game, "League of Legends.exe"), "");
        File.WriteAllText(Path.Combine(Game, "HID.dll"), "test fixture, never executed");
    }
    private static byte[] Wad(byte payload = 42)
    {
        var bytes = new byte[305]; bytes[0] = (byte)'R'; bytes[1] = (byte)'W'; bytes[2] = 3;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(268), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(280), 304);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(284), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(288), 1);
        bytes[304] = payload; return bytes;
    }
    private string Package(string name, byte[]? bytes = null) { string path = Path.Combine(_root, name); File.WriteAllBytes(path, bytes ?? Wad()); return path; }

    [Fact]
    public async Task ReferenceSampleCanBeStagedAndClearedInIsolatedGameDirectory()
    {
        if (Environment.GetEnvironmentVariable("RESOURCE_SKIN_SAMPLE") is not { Length: > 0 } sample) return;
        using var service = new ResourceSkinService(Journal, () => false, monitor: false);
        await service.ApplyAsync(Game, [sample]);
        var file = Assert.Single(service.State.Files);
        Assert.True(file.Entries > 0); Assert.Equal(new FileInfo(sample).Length, file.Bytes);
        await service.ClearAsync(); Assert.Empty(Directory.GetFiles(Mount));
    }

    [Fact]
    public async Task DirectWadMountSurvivesRestartAndOnlyOwnFilesAreCleared()
    {
        Directory.CreateDirectory(Mount); var foreign = Path.Combine(Mount, "wind-user.wad.client"); File.WriteAllBytes(foreign, Wad());
        using (var service = new ResourceSkinService(Journal, () => false, monitor: false))
        {
            await service.ApplyAsync(Game, [Package("超帅黑夜特效.wad.client")]);
            var file = Assert.Single(service.State.Files); Assert.Equal((uint)1, file.Entries);
            Assert.Equal("超帅黑夜特效.wad.client", file.Source); Assert.True(File.Exists(Path.Combine(Mount, file.Name)));
        }
        using var restored = new ResourceSkinService(Journal, () => false, monitor: false);
        Assert.Single(restored.State.Files); await restored.ClearAsync(); Assert.Empty(restored.State.Files);
        Assert.Equal([foreign], Directory.GetFiles(Mount)); Assert.True(File.Exists(Path.Combine(Game, "HID.dll")));
    }
    [Fact]
    public async Task ArchiveEntryNamesNeverBecomePathsAndReplacementRemovesPreviousSet()
    {
        using var service = new ResourceSkinService(Journal, () => false, monitor: false);
        await service.ApplyAsync(Game, [Package("first.wad.client")]); var first = service.State.Files.Single().Name;
        string archive = Path.Combine(_root, "测试.fantome");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            using var stream = zip.CreateEntry("../../escape.wad.client").Open(); stream.Write(Wad(43));
        }
        await service.ApplyAsync(Game, [archive]);
        Assert.False(File.Exists(Path.Combine(Mount, first))); Assert.Single(Directory.GetFiles(Mount));
        Assert.Single(service.State.Files); Assert.False(File.Exists(Path.Combine(_root, "escape.wad.client")));
    }
    [Fact]
    public async Task CorruptArchiveFailsBeforeChangingExistingMount()
    {
        using var service = new ResourceSkinService(Journal, () => false, monitor: false);
        await service.ApplyAsync(Game, [Package("first.wad.client")]); var first = service.State.Files.Single();
        var corrupt = Wad(); BinaryPrimitives.WriteUInt32LittleEndian(corrupt.AsSpan(280), 99999);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ApplyAsync(Game, [Package("bad.wad.client", corrupt)]));
        Assert.Equal(first, Assert.Single(service.State.Files)); Assert.Single(Directory.GetFiles(Mount));
    }
    [Fact]
    public async Task ModifiedOwnedFileIsPreservedAndRetainsOwnership()
    {
        using var service = new ResourceSkinService(Journal, () => false, monitor: false);
        await service.ApplyAsync(Game, [Package("first.wad.client")]); var path = Path.Combine(Mount, service.State.Files.Single().Name);
        File.WriteAllBytes(path, Wad(99)); await Assert.ThrowsAsync<IOException>(() => service.ClearAsync());
        Assert.True(File.Exists(path)); Assert.Single(service.State.Files);
    }
    [Fact]
    public async Task ActiveGameMissingLoaderAndWrongEngineBlockWrites()
    {
        bool running = true; using var service = new ResourceSkinService(Journal, () => running, monitor: false);
        var package = Package("test.wad.client");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(Game, [package]));
        running = false; File.Delete(Path.Combine(Game, "HID.dll"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(Game, [package]));
        using var denied = new ResourceSkinService(Journal, () => false, () => false, monitor: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => denied.ApplyAsync(Game, [package]));
        Assert.False(Directory.Exists(Mount));
    }
    [Fact]
    public async Task TamperedJournalCannotDeleteAnotherFileOrBeOverwritten()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Journal)!);
        File.WriteAllText(Journal, "{\"GameDirectory\":\"\",\"Files\":[{\"Name\":\"../foreign.wad.client\"}]}");
        using var service = new ResourceSkinService(Journal, () => false, monitor: false);
        Assert.NotNull(service.CleanupError);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ClearAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ApplyAsync(Game, [Package("test.wad.client")]));
        Assert.Contains("foreign", File.ReadAllText(Journal));
    }
    [Fact]
    public async Task SwitchingEnginesClearsResourcesAndStopsOverlay()
    {
        using var resources = new ResourceSkinService(Journal, () => false, monitor: false);
        await resources.ApplyAsync(Game, [Package("test.wad.client")]);
        var memory = new Memory(); var files = new Files(); var settings = new Settings();
        settings.Value.SkinEngineMode = SkinEngineMode.Resource;
        var engines = new SkinEngineCoordinator(memory, files, settings, () => false, resources);
        await engines.SelectAsync(SkinEngineMode.File); Assert.Empty(resources.State.Files);
        files.Running = true; memory.SetEnabled(true);
        await engines.SelectAsync(SkinEngineMode.Resource);
        Assert.False(files.Running); Assert.False(memory.Enabled); Assert.Equal(SkinEngineMode.Resource, settings.Value.SkinEngineMode);
    }
    [Fact]
    public async Task GameExitMonitorCleansOwnedFiles()
    {
        int running = 0;
        using var service = new ResourceSkinService(Journal, () => Volatile.Read(ref running) == 1);
        await service.ApplyAsync(Game, [Package("test.wad.client")]); Volatile.Write(ref running, 1);
        await Task.Delay(2500); Volatile.Write(ref running, 0);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        while (service.State.Files.Count > 0) await Task.Delay(50, timeout.Token);
        Assert.Empty(Directory.GetFiles(Mount));
    }
    [Fact]
    public void ResourcePageFitsAtNarrowWidthAndDoesNotSwitchModeWhileBrowsing() => MatchListScrollingTests.OnUiThread(() =>
    {
        var engines = new Engines(); using var service = new ResourceSkinService(Journal, () => false, monitor: false);
        using var host = new Form { Width = 800, Height = 650 };
        using var page = new ResourceSkinForm(service, engines, new Settings()); host.Controls.Add(page); host.Show();
        System.Windows.Forms.Application.DoEvents(); Assert.Equal(0, engines.Switches);
        var button = page.Controls.Find("ResourceApply", true).Single();
        Assert.True(page.ClientRectangle.Contains(page.PointToClient(button.PointToScreen(new Point(button.Width / 2, button.Height / 2)))));
    });
    private sealed class Engines : ISkinEngineCoordinator { public int Switches; public Task SelectAsync(SkinEngineMode mode, CancellationToken token = default) { Switches++; return Task.CompletedTask; } }
    private sealed class Settings : IApplicationSettingsStore
    {
        public AssistantSettings Value = new(); public AssistantSettings Load() => Value;
        public void Save(AssistantSettings value) => Value = value; public string GetStoragePath() => "";
    }
    private sealed class Memory : ISkinCoreService
    {
        public bool Enabled { get; private set; } public void SetEnabled(bool enabled) => Enabled = enabled;
        public void InvalidateSession() { } public Task SuspendAsync(CancellationToken token = default) { Enabled = false; return Task.CompletedTask; }
        public Task<SkinCoreReply> GetCatalogAsync(CancellationToken token = default) => throw new NotImplementedException();
        public Task<SkinCoreReply> ApplyAsync(string session, string entry, CancellationToken token = default) => throw new NotImplementedException();
        public Task<SkinCoreReply> RestoreAsync(string session, CancellationToken token = default) => throw new NotImplementedException();
        public Task<SkinCoreReply> StopAsync(string session, CancellationToken token = default) => throw new NotImplementedException();
    }
    private sealed class Files : ICslolSkinService
    {
        public bool Running; public bool IsAvailable => true; public bool IsRunning => Running;
        public IReadOnlyList<CslolSkinEntry> Catalog => []; public event Action<string>? StatusChanged { add { } remove { } }
        public Task<IReadOnlyList<CslolSkinEntry>> LoadCatalogAsync(CancellationToken token = default) => Task.FromResult(Catalog);
        public Task<CslolApplyResult> ApplyAsync(string gamePath, IReadOnlyList<CslolSkinEntry> entries, bool ignoreConflict = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CslolApplyResult> StopAsync() { Running = false; return Task.FromResult(new CslolApplyResult { Ok = true }); }
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
