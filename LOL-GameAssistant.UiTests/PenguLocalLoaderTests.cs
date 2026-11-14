using LOL_GameAssistant.Infrastructure.Pengu;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class PenguLocalLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sc-local-pengu", Guid.NewGuid().ToString("N"));
    private string Settings => Path.Combine(_root, "settings.json");
    private string CreateClone(string name = "clone")
    {
        string clone = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(clone, "loader", "src-tauri"));
        Directory.CreateDirectory(Path.Combine(clone, "bin"));
        File.WriteAllText(Path.Combine(clone, "loader", "src-tauri", "tauri.conf.json"), "{\"package\":{\"version\":\"1.2.0\"}}");
        File.WriteAllText(Path.Combine(clone, "bin", "pengu-loader.exe"), "local build");
        File.WriteAllText(Path.Combine(clone, "bin", "core.dll"), "local core");
        return clone;
    }

    [Fact]
    public void UsesLocalBinAndPersistsSourceSelection()
    {
        string clone = CreateClone();
        var service = new PenguLocalLoaderService(Settings, clone);
        var prepared = service.Prepare();
        Assert.Equal("1.2.0", prepared.Version);
        Assert.Equal(Path.Combine(clone, "bin", "core.dll"), prepared.CorePath);
        Assert.Equal(Path.Combine(clone, "bin", "plugins"), prepared.PluginsDirectory);
        string other = CreateClone("other");
        service.SelectDirectory(other);
        Assert.Equal(other, new PenguLocalLoaderService(Settings, clone).SourceDirectory);
    }

    [Fact]
    public void CoreWorksWithoutManagementFrontend()
    {
        string clone = CreateClone();
        File.Delete(Path.Combine(clone, "bin", "pengu-loader.exe"));
        Directory.CreateDirectory(Path.Combine(clone, "loader", "src-tauri", "target", "debug"));
        File.WriteAllText(Path.Combine(clone, "loader", "src-tauri", "target", "debug", "pengu-loader.exe"), "dev only");
        File.WriteAllText(Path.Combine(clone, "bin", "Pengu Loader.exe"), "old version");
        Assert.Equal(Path.Combine(clone, "bin", "core.dll"), new PenguLocalLoaderService(Settings, clone).Prepare().CorePath);
        Assert.False(File.Exists(Settings));
    }

    [Fact]
    public void HonorsConfiguredPluginDirectoryAndRejectsRelativePaths()
    {
        string clone = CreateClone();
        string custom = Path.Combine(_root, "my-plugins");
        string config = Path.Combine(clone, "bin", "config");
        File.WriteAllText(config, $"[app]\nplugins_dir = {custom}\n[client]\nplugins_dir = ignored");
        Assert.Equal(custom, new PenguLocalLoaderService(Settings, clone).Prepare().PluginsDirectory);
        File.WriteAllText(config, "[app]\nplugins_dir = ../elsewhere");
        Assert.Throws<InvalidOperationException>(() => new PenguLocalLoaderService(Settings, clone).Prepare());
    }

    [Fact]
    public void MissingCoreAndInvalidSelectionLeavePreviousSourceIntact()
    {
        string clone = CreateClone();
        var service = new PenguLocalLoaderService(Settings, clone);
        service.SelectDirectory(clone);
        Assert.Throws<InvalidOperationException>(() => service.SelectDirectory(_root));
        Assert.Equal(clone, service.SourceDirectory);
        File.Delete(Path.Combine(clone, "bin", "core.dll"));
        Assert.Throws<InvalidOperationException>(() => service.Prepare());
    }

    [Fact]
    public void LocalActivationModesUseTheClonesIniKeys()
    {
        string clone = CreateClone();
        string bin = Path.Combine(clone, "bin");
        File.WriteAllText(Path.Combine(bin, "config"), "[app]\nactivation_mode = 1\nleague_dir = ");
        Assert.Contains("没有配置游戏目录", PenguLoaderActivation.Read(bin).Message);
        File.WriteAllText(Path.Combine(bin, "config"), "[app]\nactivation_mode = 2");
        Assert.Null(PenguLoaderActivation.Read(bin).Activated);
        Assert.Contains("按需", PenguLoaderActivation.Read(bin).Message);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
