using LOL_GameAssistant.Infrastructure.Pengu;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class PenguFirstRunSetupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pengu-first-run-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InitializesOnlyOnceIncludingConcurrentStartupAndLaterLaunches()
    {
        int calls = 0;
        var setup = new PenguFirstRunSetup(_root, async _ => { calls++; await Task.Yield(); });
        var results = await Task.WhenAll(setup.EnsureInitializedAsync(), setup.EnsureInitializedAsync());
        Assert.Equal(1, calls);
        Assert.Single(results, value => value);
        var nextLaunch = new PenguFirstRunSetup(_root, _ => throw new Exception("Must preserve the user's later choices"));
        Assert.False(await nextLaunch.EnsureInitializedAsync());
    }

    [Theory]
    [InlineData("pengu-loader-local.json")]
    [InlineData("pengu-plugin.json")]
    public async Task ExistingUsersKeepTheirConfiguration(string file)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, file), "\"existing directory\"");
        var setup = new PenguFirstRunSetup(_root, _ => throw new Exception("Do not initialize existing users"));
        Assert.False(await setup.EnsureInitializedAsync());
        Assert.False(File.Exists(Path.Combine(_root, "pengu-first-run.state")));
    }

    [Fact]
    public async Task RetriesAnInterruptedInstallationEvenAfterLoaderPathWasSaved()
    {
        var interrupted = new PenguFirstRunSetup(_root, _ =>
        {
            File.WriteAllText(Path.Combine(_root, "pengu-loader-local.json"), "\"partial install\"");
            throw new IOException("Download or activation failed");
        });
        await Assert.ThrowsAsync<IOException>(() => interrupted.EnsureInitializedAsync());
        bool initialized = false;
        var retry = new PenguFirstRunSetup(_root, _ => { initialized = true; return Task.CompletedTask; });
        Assert.True(await retry.EnsureInitializedAsync());
        Assert.True(initialized);
        Assert.Equal("complete", File.ReadAllText(Path.Combine(_root, "pengu-first-run.state")));
    }

    [Fact]
    public void PresetMatchesTheRequestedVisualAndNativeConfiguration()
    {
        var visualService = new PenguVisualSettingsService(() => _root);
        visualService.Save(PenguFirstRunSetup.VisualDefaults);
        var visual = visualService.Read();
        Assert.True(visual.Enabled && visual.ShowBadges && visual.ShowPlayerStats && visual.ShowCounters);
        Assert.False(visual.WallpaperEnabled || visual.HideNavigation);
        Assert.Equal("auto", visual.TierMode);
        Assert.Empty(visual.WallpaperUrl);
        var clientService = new PenguClientOptionsService(() => Path.Combine(_root, "config"));
        clientService.Save(PenguFirstRunSetup.ClientDefaults, developerTools: true);
        var client = clientService.Read();
        Assert.False(client.OptimizedClient || client.LowSpecMode || client.SilentMode);
        Assert.True(client.RiotClientApi && client.InsecureMode);
        // Changing an exposed option must preserve the developer-tools setting.
        clientService.Save(new() { OptimizedClient = true });
        Assert.Contains("use_devtools = true", File.ReadAllText(Path.Combine(_root, "config")));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
