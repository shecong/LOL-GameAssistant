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

