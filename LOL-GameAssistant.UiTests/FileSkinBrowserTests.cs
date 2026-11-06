using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Domain.Skins;
using Xunit;

namespace LOL_GameAssistant.UiTests;
public sealed class FileSkinBrowserTests
{
    [Fact]
    public void PreferencesRoundTripAndNullCollectionsDoNotBreakBrowser()
    {
        var root = Path.Combine(Path.GetTempPath(), "file-skin-preferences-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "browser.json");
        try
        {
            var store = new LOL_GameAssistant.Infrastructure.Skins.FileSkinPreferencesStore(path);
            store.Save(new() { Favorites = ["Annie/one"], Presets = new() { ["排位"] = ["Annie/one", "Vayne/one"] } });
            var saved = store.Load(); Assert.Contains("Annie/one", saved.Favorites); Assert.Equal(2, saved.Presets["排位"].Length);
            File.WriteAllText(path, "{\"Favorites\":null,\"Presets\":null}");
            Assert.Empty(store.Load().Favorites); Assert.Empty(store.Load().Presets);
        }
        finally { if (File.Exists(path)) File.Delete(path); if (Directory.Exists(root)) Directory.Delete(root); }
    }
    private static readonly CslolSkinEntry First = new() { ChampionKey = "Annie", SkinName = "one", FileName = "Annie/one" };
    private static readonly CslolSkinEntry Second = new() { ChampionKey = "Annie", SkinName = "two", FileName = "Annie/two" };
    private static readonly CslolSkinEntry Vayne = new() { ChampionKey = "Vayne", SkinName = "one", FileName = "Vayne/one" };
    [Fact]
    public void SelectionReplacesSameHeroAndCanBeToggledOff()
    {
        var model = new FileSkinBrowserModel(); model.SetCatalog([First, Second, Vayne]);
        model.Toggle(First); model.Toggle(Vayne); model.Toggle(Second);
        Assert.Equal(2, model.Selected.Count); Assert.False(model.IsSelected(First)); Assert.True(model.IsSelected(Second));
        model.Toggle(Second); Assert.Equal(Vayne, Assert.Single(model.Selected));
        Assert.Throws<InvalidOperationException>(() => model.Toggle(First with { FileName = "foreign" }));
    }
    [Fact]
    public void PresetsDropUnavailableEntriesAndCatalogChangesRefreshSelections()
    {
        var model = new FileSkinBrowserModel(); model.SetCatalog([First, Second, Vayne]);
        model.Restore([First.FileName, "missing", Second.FileName, Vayne.FileName]);
        Assert.Equal(2, model.Selected.Count); Assert.True(model.IsSelected(Second));
        var renamed = Second with { SkinName = "new name" }; model.SetCatalog([renamed]);
        Assert.Equal(renamed, Assert.Single(model.Selected));
    }
    [Fact]
    public void FilteringUsesPackageIdentityForFavoritesAndSelections()
    {
        var model = new FileSkinBrowserModel(); model.SetCatalog([First, Second, Vayne]); model.Toggle(Second);
        var favorites = new HashSet<string> { Vayne.FileName };
        Assert.Equal(Vayne, Assert.Single(model.Filter(null, _ => true, favorites, true, false, true)));
        Assert.Equal(Second, Assert.Single(model.Filter("ANNIE", _ => true, favorites, false, true, true)));
        Assert.Empty(model.Filter("Annie", _ => true, favorites, true, false, true));
    }
    [Fact]
    public async Task FileSwitchDrainsMemoryBeforeSavingOwnership()
    {
        var memory = new Memory { Enabled = true }; var files = new Files(); var settings = new Settings();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        memory.Suspend = async () => { memory.Enabled = false; entered.SetResult(); await finish.Task; };
        var coordinator = new SkinEngineCoordinator(memory, files, settings, () => false);
        var switching = coordinator.SelectAsync(SkinEngineMode.File); await entered.Task;
        Assert.Equal(SkinEngineMode.Memory, settings.Value.SkinEngineMode);
        finish.SetResult(); await switching;
        Assert.False(memory.Enabled); Assert.Equal(SkinEngineMode.File, settings.Value.SkinEngineMode);
    }
    [Fact]
    public async Task ActiveGameBlocksSwitchAndStopFailureRetainsFileOwnership()
    {
        var memory = new Memory(); var files = new Files(); var settings = new Settings();
        var coordinator = new SkinEngineCoordinator(memory, files, settings, () => true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.SelectAsync(SkinEngineMode.File));
        Assert.Equal(0, memory.Suspends);
        settings.Value.SkinEngineMode = SkinEngineMode.File; files.Running = true; files.StopOk = false;
        coordinator = new(memory, files, settings, () => false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.SelectAsync(SkinEngineMode.Memory));
        Assert.Equal(SkinEngineMode.File, settings.Value.SkinEngineMode);
        files.StopOk = true; await coordinator.SelectAsync(SkinEngineMode.Memory);
        Assert.False(files.IsRunning); Assert.Equal(SkinEngineMode.Memory, settings.Value.SkinEngineMode);
    }
    [Fact]
    public async Task ThreeSwitchesAreExclusiveAndCanAllBeOff()
    {
        var memory = new Memory(); var files = new Files(); var resources = new Resources(); var settings = new Settings();
        var coordinator = new SkinEngineCoordinator(memory, files, settings, () => false, resources);
        void AssertOnly(SkinEngineMode active)
        {
            foreach (var mode in new[] { SkinEngineMode.Memory, SkinEngineMode.Resource, SkinEngineMode.File }) Assert.Equal(mode == active, coordinator.IsEnabled(mode));
        }
        AssertOnly(SkinEngineMode.None);
        await coordinator.SetEnabledAsync(SkinEngineMode.Memory, true); AssertOnly(SkinEngineMode.Memory); Assert.True(settings.Value.SkinCoreEnabled);
        await coordinator.SetEnabledAsync(SkinEngineMode.File, true); AssertOnly(SkinEngineMode.File); Assert.False(memory.Enabled);
        files.Running = true;
        await coordinator.SetEnabledAsync(SkinEngineMode.Resource, true); AssertOnly(SkinEngineMode.Resource); Assert.False(files.Running);
        resources.Mounted = true;
        await coordinator.SetEnabledAsync(SkinEngineMode.File, false); AssertOnly(SkinEngineMode.Resource); Assert.True(resources.Mounted);
        await coordinator.SetEnabledAsync(SkinEngineMode.Resource, false); AssertOnly(SkinEngineMode.None); Assert.False(resources.Mounted);
        Assert.Equal(SkinEngineMode.None, settings.Value.SkinEngineMode);
    }
    [Fact]
    public async Task CleanupFailureRetainsExistingSwitchAndPreventsNewEngine()
    {
        var memory = new Memory(); var files = new Files(); var resources = new Resources { Mounted = true, FailClear = true };
        var settings = new Settings(); settings.Value.SkinEngineMode = SkinEngineMode.Resource;
        var coordinator = new SkinEngineCoordinator(memory, files, settings, () => false, resources);
        await Assert.ThrowsAsync<IOException>(() => coordinator.SetEnabledAsync(SkinEngineMode.Memory, true));
        Assert.True(coordinator.IsEnabled(SkinEngineMode.Resource)); Assert.False(memory.Enabled);
        await Assert.ThrowsAsync<IOException>(() => coordinator.SetEnabledAsync(SkinEngineMode.Resource, false));
        Assert.True(coordinator.IsEnabled(SkinEngineMode.Resource));
    }
    [Fact]
    public async Task ConcurrentSwitchesWaitForCleanupAndNeverEnableTwoEngines()
    {
        var memory = new Memory(); var files = new Files(); var resources = new Resources { Mounted = true };
        var settings = new Settings(); settings.Value.SkinEngineMode = SkinEngineMode.Resource;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        resources.BeforeClear = async () => { entered.TrySetResult(); await release.Task; };
        var coordinator = new SkinEngineCoordinator(memory, files, settings, () => false, resources);
        int violations = 0;
        coordinator.Changed += () => { if (new[] { SkinEngineMode.Memory, SkinEngineMode.File, SkinEngineMode.Resource }.Count(coordinator.IsEnabled) > 1) Interlocked.Increment(ref violations); };
        var first = coordinator.SetEnabledAsync(SkinEngineMode.Memory, true); await entered.Task;
        var second = coordinator.SetEnabledAsync(SkinEngineMode.File, true);
        Assert.False(first.IsCompleted); Assert.False(second.IsCompleted); Assert.True(coordinator.IsEnabled(SkinEngineMode.Resource));
        release.SetResult(); await Task.WhenAll(first, second);
        Assert.True(coordinator.IsEnabled(SkinEngineMode.File)); Assert.False(memory.Enabled); Assert.False(resources.Mounted); Assert.Equal(0, violations);
    }
    [Fact]
    public async Task NativeSwitchCanBeReenabledAndFileOverlayCanBeStoppedDuringMatch()
    {
        var memory = new Memory(); var files = new Files(); var settings = new Settings();
        var coordinator = new SkinEngineCoordinator(memory, files, settings, () => true);
        await coordinator.SetEnabledAsync(SkinEngineMode.Memory, true);
        await coordinator.SetEnabledAsync(SkinEngineMode.Memory, false); Assert.Equal(SkinEngineMode.None, settings.Value.SkinEngineMode);
        await coordinator.SetEnabledAsync(SkinEngineMode.Memory, true); Assert.True(memory.Enabled);
        settings.Value.SkinEngineMode = SkinEngineMode.File; memory.SetEnabled(false); files.Running = true;
        await coordinator.SetEnabledAsync(SkinEngineMode.File, false);
        Assert.False(coordinator.IsEnabled(SkinEngineMode.File)); Assert.False(files.Running);
        Assert.Equal(SkinEngineMode.None, settings.Value.SkinEngineMode);
    }
    [Theory]
    [InlineData(SkinEngineMode.None)]
    [InlineData(SkinEngineMode.Memory)]
    [InlineData(SkinEngineMode.File)]
    public async Task LeavingResourceModeDisablesLoaderEvenAfterMountsWereCleared(SkinEngineMode target)
    {
        var settings = new Settings(); settings.Value.SkinEngineMode = SkinEngineMode.Resource;
        settings.Value.CslolGamePath = "test-game";
        var resources = new Resources { Mounted = true };
        var loader = new Loader { BeforeDisable = () => Assert.False(resources.Mounted) };
        var coordinator = new SkinEngineCoordinator(new Memory(), new Files(), settings, () => false, resources, loader);
        if (target == SkinEngineMode.None) await coordinator.SetEnabledAsync(SkinEngineMode.Resource, false);
        else await coordinator.SelectAsync(target);
        Assert.Equal(new[] { "test", "test-game" }, loader.Disabled); Assert.Equal(target, settings.Value.SkinEngineMode);

        // Clearing WADs earlier does not imply that the DLL is already inactive.
        settings.Value.SkinEngineMode = SkinEngineMode.Resource;
        await coordinator.SetEnabledAsync(SkinEngineMode.Resource, false);
        Assert.Equal(4, loader.Disabled.Count);
    }

    [Fact]
    public async Task LoaderDisableFailureRetainsModeAndActiveMatchNeverTouchesLoader()
    {
        var settings = new Settings(); settings.Value.SkinEngineMode = SkinEngineMode.Resource;
        settings.Value.CslolGamePath = "test-game";
        bool running = false;
        var loader = new Loader { BeforeDisable = () => throw new IOException("loader busy") };
        var coordinator = new SkinEngineCoordinator(new Memory(), new Files(), settings, () => running, new Resources(), loader);
        await Assert.ThrowsAsync<IOException>(() => coordinator.SetEnabledAsync(SkinEngineMode.Resource, false));
        Assert.Equal(SkinEngineMode.Resource, settings.Value.SkinEngineMode);
        running = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.SetEnabledAsync(SkinEngineMode.Resource, false));
        Assert.Single(loader.Disabled); Assert.Equal(SkinEngineMode.Resource, settings.Value.SkinEngineMode);
    }

    private sealed class Loader : IResourceSkinLoader
    {
        public List<string> Disabled = []; public Action? BeforeDisable;
        public Task EnsureInstalledAsync(string gameDirectory, CancellationToken token = default) => throw new NotImplementedException();
        public Task DisableAsync(string gameDirectory, CancellationToken token = default)
        {
            Disabled.Add(gameDirectory); BeforeDisable?.Invoke(); return Task.CompletedTask;
        }
    }

    private sealed class Resources : IResourceSkinService
    {
        public bool Mounted, FailClear; public Func<Task>? BeforeClear;
        public ResourceSkinState State => new("test", Mounted ? [new("owned.wad.client", "test", "", 305, 1)] : []);
        public Task ApplyAsync(string gameDirectory, IReadOnlyList<string> packages, CancellationToken token = default) => throw new NotImplementedException();
        public async Task ClearAsync(CancellationToken token = default)
        {
            if (FailClear) throw new IOException("资源占用");
            if (BeforeClear != null) await BeforeClear(); Mounted = false;
        }
    }
    private sealed class Settings : IApplicationSettingsStore
    {
        public AssistantSettings Value = new(); public AssistantSettings Load() => Value;
        public void Save(AssistantSettings config) => Value = config; public string GetStoragePath() => "";
    }
    private sealed class Memory : ISkinCoreService
    {
        public bool Enabled { get; set; } public int Suspends; public Func<Task>? Suspend;
        public void SetEnabled(bool enabled) => Enabled = enabled; public void InvalidateSession() { }
        public async Task SuspendAsync(CancellationToken token = default) { Suspends++; if (Suspend != null) await Suspend(); else Enabled = false; }
        public Task<SkinCoreReply> GetCatalogAsync(CancellationToken token = default) => throw new NotImplementedException();
        public Task<SkinCoreReply> ApplyAsync(string session, string entry, CancellationToken token = default) => throw new NotImplementedException();
        public Task<SkinCoreReply> RestoreAsync(string session, CancellationToken token = default) => throw new NotImplementedException();
        public Task<SkinCoreReply> StopAsync(string session, CancellationToken token = default) => throw new NotImplementedException();
    }
    private sealed class Files : ICslolSkinService
    {
        public bool Running, StopOk = true; public bool IsAvailable => true; public bool IsRunning => Running;
        public IReadOnlyList<CslolSkinEntry> Catalog => [];
        public event Action<string>? StatusChanged { add { } remove { } }
        public Task<IReadOnlyList<CslolSkinEntry>> LoadCatalogAsync(CancellationToken token = default) => Task.FromResult(Catalog);
        public Task<CslolApplyResult> ApplyAsync(string gamePath, IReadOnlyList<CslolSkinEntry> entries, bool ignoreConflict = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CslolApplyResult> StopAsync() { if (StopOk) Running = false; return Task.FromResult(new CslolApplyResult { Ok = StopOk }); }
    }
}

