using LOL_GameAssistant.Infrastructure.Pengu;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class PenguClientOptionsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sc-pengu-options", Guid.NewGuid().ToString("N"));
    private string Config => Path.Combine(_root, "config");

    [Fact]
    public void DefaultsMatchNativeCoreAndDoNotCreateConfig()
    {
        Assert.Equal(new PenguClientOptions(), new PenguClientOptionsService(() => Config).Read());
        Assert.False(File.Exists(Config));
    }

    [Fact]
    public void SavePreservesPathsUnknownOptionsAndCommentsAndRemovesConflictingDuplicates()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Config, "[app]\nplugins_dir = E:\\我的插件\n; keep comment\n[client]\nuse_devtools = true\nisecure_mode = false\noptimized_client = false\n[other]\noptimized_client = false\ncustom = untouched\n");
        var service = new PenguClientOptionsService(() => Config);
        var expected = new PenguClientOptions { OptimizedClient = true, LowSpecMode = true, SilentMode = true, RiotClientApi = true, InsecureMode = true };
        service.Save(expected);
        Assert.Equal(expected, service.Read());
        string content = File.ReadAllText(Config);
        Assert.Contains("plugins_dir = E:\\我的插件", content);
        Assert.Contains("; keep comment", content);
        Assert.Contains("use_devtools = true", content);
        Assert.Contains("custom = untouched", content);
        Assert.Contains("insecure_mode = true", content);
        Assert.Contains("isecure_mode = true", content);
        Assert.Single(File.ReadAllLines(Config), line => line.StartsWith("optimized_client ="));
        Assert.False(File.ReadAllBytes(Config).Take(3).SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }));
        service.Save(new() { OptimizedClient = false });
        Assert.Equal(new PenguClientOptions { OptimizedClient = false }, service.Read());
    }

    [Fact]
    public void ReadsNumericValuesAndHistoricalInsecureKeyLikeNativeCore()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Config, "[client]\nsuper_potato = 1\nsilent_mode = 0\ninsecure_mode = true\nisecure_mode = false\n");
        var service = new PenguClientOptionsService(() => Config);
        Assert.True(service.Read().LowSpecMode);
        Assert.False(service.Read().SilentMode);
        Assert.False(service.Read().InsecureMode);
        File.WriteAllText(Config, "[client]\ninsecure_mode = true\n");
        Assert.True(service.Read().InsecureMode);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
