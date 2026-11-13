using System.Text.Json;
using LOL_GameAssistant.Infrastructure.Pengu;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class PenguPluginTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sc-pengu-tests", Guid.NewGuid().ToString("N"));
    private readonly PenguPluginService _service;
    private readonly string _target;

    public PenguPluginTests()
    {
        string bundle = Path.Combine(_root, "bundle");
        string plugins = Path.Combine(_root, "loader", "plugins");
        Directory.CreateDirectory(bundle);
        Directory.CreateDirectory(plugins);
        File.WriteAllText(Path.Combine(bundle, "index.js"), "const ROOT_ID = 'lol-game-assistant-visual-root';");
        File.WriteAllText(Path.Combine(bundle, "pengu.yml"), $"id: {PenguPluginService.PluginId}");
        File.WriteAllText(Path.Combine(bundle, "README.md"), "test");
        _service = new(bundle, Path.Combine(_root, "settings.json"));
        _service.SelectDirectory(Path.GetDirectoryName(plugins)!);
        _target = Path.Combine(plugins, PenguPluginService.PluginId);
    }

    [Fact]
    public void InstallationDisableUpdateAndEnablePreserveOtherFiles()
    {
        Assert.False(_service.Inspect().Installed);
        _service.Install();
        Assert.True(_service.Inspect().Enabled);
        Assert.False(_service.Inspect().Loaded);
        File.WriteAllText(Path.Combine(_target, "my-notes.txt"), "keep");
        _service.SetEnabled(false);
        _service.Install();
        Assert.True(_service.Inspect().Installed);
        Assert.False(_service.Inspect().Enabled);
        _service.SetEnabled(true);
        Assert.True(_service.Inspect().Enabled);
        _service.Uninstall();
        Assert.Equal("keep", File.ReadAllText(Path.Combine(_target, "my-notes.txt")));
        Assert.False(File.Exists(Path.Combine(_target, "index.js")));
        Assert.False(_service.Inspect().Installed);
        _service.Install();
        Assert.True(_service.Inspect().Enabled);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(_target, "my-notes.txt")));
    }

    [Fact]
    public void ForeignDirectoryIsNeverOverwrittenOrRemoved()
    {
        Directory.CreateDirectory(_target);
        string entry = Path.Combine(_target, "index.js");
        File.WriteAllText(entry, "some other plugin");
        Assert.Throws<InvalidOperationException>(_service.Install);
        Assert.Throws<InvalidOperationException>(() => _service.SetEnabled(false));
        Assert.Throws<InvalidOperationException>(_service.Uninstall);
        Assert.Equal("some other plugin", File.ReadAllText(entry));
    }

    [Fact]
    public void OnlyFreshActiveHeartbeatCountsAsLoaded()
    {
        _service.Install();
        void Heartbeat(DateTimeOffset timestamp, bool active) => File.WriteAllText(Path.Combine(_target, "runtime.json"),
            JsonSerializer.Serialize(new { timestamp, active, panelMounted = true, version = "0.2.0", targets = 5, badges = 4 }));
        Heartbeat(DateTimeOffset.UtcNow.AddMinutes(-2), true);
        Assert.False(_service.Inspect().Loaded);
        Heartbeat(DateTimeOffset.UtcNow, false);
        Assert.False(_service.Inspect().Loaded);
        Heartbeat(DateTimeOffset.UtcNow, true);
        Assert.True(_service.Inspect().Loaded);
        Assert.Contains("头像 5 / 角标 4", _service.Inspect().Message);
        _service.SetEnabled(false);
        Assert.True(_service.Inspect().Loaded);
        Assert.Contains("当前客户端仍在运行", _service.Inspect().Message);
        File.WriteAllText(Path.Combine(_target, "runtime.json"), "{");
        Assert.False(_service.Inspect().Loaded);
    }

    [Fact]
    public void DirectorySelectionRequiresAnExistingPluginsFolder()
    {
        Assert.Throws<InvalidOperationException>(() => _service.SelectDirectory(Path.Combine(_root, "missing")));
        Assert.EndsWith("plugins", _service.PluginsDirectory);
    }

    [Fact]
    public void CheckDistinguishesInstalledFilesFromLoaderAndPluginLoading()
    {
        _service.Install();
        File.WriteAllText(Path.Combine(_root, "loader", "version"), "1.1.6+test");
        var service = new PenguPluginService(Path.Combine(_root, "bundle"), Path.Combine(_root, "settings.json"),
            _ => new(true, null), _ => new(true, "已激活（全局模式）"));
        var status = service.Check();
        Assert.True(status.Installed);
        Assert.False(status.Loaded);
        Assert.Contains("LOL 客户端：正在运行", status.Message);
        Assert.Contains("Loader 模块：未在客户端进程中检测到", status.Message);
        Assert.Contains("所选 Loader 版本：1.1.6+test", status.Message);
        Assert.Contains("请接受 Loader 的客户端重启提示", status.Message);
    }

    [Fact]
    public void CheckReportsLoadedLoaderFromAnotherDirectoryWithoutClaimingPluginLoaded()
    {
        _service.Install();
        var service = new PenguPluginService(Path.Combine(_root, "bundle"), Path.Combine(_root, "settings.json"),
            _ => new(true, Path.Combine(_root, "another-loader")));
        var status = service.Check();
        Assert.False(status.Loaded);
        Assert.Contains("客户端加载的是另一目录的 Loader", status.Message);
    }

    [Fact]
    public void CheckReportsUnreadableModulesAsUnknown()
    {
        _service.Install();
        var service = new PenguPluginService(Path.Combine(_root, "bundle"), Path.Combine(_root, "settings.json"),
            _ => new(true, null, "权限不足"), _ => new(true, "已激活（全局模式）"));
        var status = service.Check();
        Assert.Contains("Loader 模块：无法确认", status.Message);
        Assert.Contains("暂时无法判断", status.Message);
    }

    [Fact]
    public void CheckExplainsThatInstalledPluginDoesNotActivateLoader()
    {
        _service.Install();
        var service = new PenguPluginService(Path.Combine(_root, "bundle"), Path.Combine(_root, "settings.json"),
            _ => new(true, null), _ => new(false, "未激活（Active Status 为 OFF）"));
        Assert.Contains("从 OFF 切换为 ON", service.Check().Message);
        Assert.Contains("不会自动开启此开关", service.Check().Message);
    }

    [Fact]
    public void ActivationEntryMustPointToSelectedLoader()
    {
        string core = Path.Combine(_root, "loader", "core.dll");
        Assert.True(PenguLoaderActivation.MatchesDebugger($"rundll32 \"{core}\", #6000 ", core));
        Assert.False(PenguLoaderActivation.MatchesDebugger($"rundll32 \"{core}.other\", #6000 ", core));
        Assert.False(PenguLoaderActivation.MatchesDebugger(null, core));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
