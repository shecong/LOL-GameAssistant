using LOL_GameAssistant.Infrastructure.Skins;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class CslolGameIndexMirrorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cslol-mirror-tests-" + Guid.NewGuid().ToString("N"));
    private string Game => Path.Combine(_root, "game with spaces");
    private string Cache => Path.Combine(_root, "cache");
    private string Wad => Path.Combine(Game, "DATA", "FINAL", "Champions", "Annie.wad.client");
    private const string RelativeWad = "DATA/FINAL/Champions/Annie.wad.client";

    public CslolGameIndexMirrorTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Wad)!);
        File.WriteAllText(Path.Combine(Game, "League of Legends.exe"), "game version");
        File.WriteAllText(Wad, "complete WAD including entry payloads");
    }

    [Fact]
    public async Task ReusesVerifiedMirrorAndRebuildsSameLengthCorruption()
    {
        var mirror = new CslolGameIndexMirror(Cache);
        string first = await mirror.PrepareAsync(Game, _ => { }, default);
        Assert.Equal(first, await mirror.PrepareAsync(Game, _ => { }, default));
        string target = Path.Combine(first, RelativeWad);
        await File.WriteAllBytesAsync(target, new byte[new FileInfo(target).Length]);
        string rebuilt = await mirror.PrepareAsync(Game, _ => { }, default);
        Assert.NotEqual(first, rebuilt);
        Assert.Equal(await File.ReadAllBytesAsync(Wad), await File.ReadAllBytesAsync(Path.Combine(rebuilt, RelativeWad)));
        Assert.Equal(rebuilt, await mirror.PrepareAsync(Game, _ => { }, default));
    }

    [Fact]
    public async Task ResourceUpdateAndDeletionInvalidateSnapshot()
    {
        var second = Path.Combine(Game, "DATA", "FINAL", "Champions", "Other.wad.client");
        await File.WriteAllTextAsync(second, "old resource");
        var mirror = new CslolGameIndexMirror(Cache);
        string first = await mirror.PrepareAsync(Game, _ => { }, default);
        await File.WriteAllTextAsync(Wad, "patched resource with new length");
        File.Delete(second);
        string patched = await mirror.PrepareAsync(Game, _ => { }, default);
        Assert.NotEqual(first, patched);
        Assert.False(File.Exists(Path.Combine(patched, "DATA", "FINAL", "Champions", "Other.wad.client")));
        Assert.Equal("patched resource with new length", await File.ReadAllTextAsync(Path.Combine(patched, RelativeWad)));
    }

    [Fact]
    public async Task CancellationDuringCopyPublishesNothingAndCleansStaging()
    {
        using var cancellation = new CancellationTokenSource();
        var mirror = new CslolGameIndexMirror(Cache);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mirror.PrepareAsync(Game,
            status => { if (status.StartsWith("正在建立")) cancellation.Cancel(); }, cancellation.Token));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Cache));
        Assert.Equal("complete WAD including entry payloads", await File.ReadAllTextAsync(Wad));
    }

    [Fact]
    public async Task UpdateDuringCopyDoesNotPublishMixedVersionCache()
    {
        var mirror = new CslolGameIndexMirror(Cache);
        bool changed = false;
        var error = await Assert.ThrowsAsync<IOException>(() => mirror.PrepareAsync(Game, status =>
        {
            if (!changed && status.StartsWith("正在建立"))
            {
                changed = true;
                File.WriteAllText(Path.Combine(Game, "League of Legends.exe"), "updated game version");
            }
        }, default));
        Assert.Contains("发生变化", error.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Cache));
    }

    [Fact]
    public async Task LockedSourceFailsWithoutPublishingPartialCache()
    {
        using var locked = new FileStream(Wad, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var mirror = new CslolGameIndexMirror(Cache);
        await Assert.ThrowsAsync<IOException>(() => mirror.PrepareAsync(Game, _ => { }, default));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Cache));
    }

    [Fact]
    public async Task CacheInsideGameDirectoryIsRejected()
    {
        var mirror = new CslolGameIndexMirror(Path.Combine(Game, "mirror"));
        await Assert.ThrowsAsync<IOException>(() => mirror.PrepareAsync(Game, _ => { }, default));
        Assert.False(Directory.Exists(Path.Combine(Game, "mirror")));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
