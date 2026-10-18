using System.Reflection;
using System.Runtime.InteropServices;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.BaseViewForm;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class MayhemFullscreenTests
{
    [Fact]
    public void TrackingPublishesIntoGameWithoutShowingDesktopWindowEvenDuringScan() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var bridge = new RecordingBridge();
        using var window = new MayhemOverlayController(new EmptyScanner(), bridge);
        SetField(window, "_tracking", true);
        SetField(window, "_mayhemActive", true);
        nint foreground = GetForegroundWindow();
        for (int i = 0; i < 3; i++) window.MaintainOverlayVisibility();
        Assert.Equal(3, bridge.Frames.Count);
        Assert.Equal(foreground, GetForegroundWindow());
        SetField(window, "_scanning", true);
        window.MaintainOverlayVisibility();
        Assert.True(bridge.Frames[^1].Scanning);
        window.StopTracking();
        window.MaintainOverlayVisibility();
        Assert.False(bridge.Enabled);
        Assert.Equal(4, bridge.Frames.Count);
    });

    [Fact]
    public void OnlyMayhemPublishesAndNativeCommandsChangeRarityAndCollapse() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var bridge = new RecordingBridge();
        using var window = new MayhemOverlayController(new EmptyScanner(), bridge);
        SetField(window, "_tracking", true);
        window.MaintainOverlayVisibility();
        Assert.Empty(bridge.Frames);
        SetField(window, "_mayhemActive", true);
        bridge.Command = 12;
        window.MaintainOverlayVisibility();
        window.MaintainOverlayVisibility();
        Assert.Equal(2, bridge.Frames[^1].Rarity);
        bridge.Command = 2;
        window.MaintainOverlayVisibility(); window.MaintainOverlayVisibility();
        Assert.True(bridge.Frames[^1].Collapsed);
        bridge.Command = 2;
        window.MaintainOverlayVisibility(); window.MaintainOverlayVisibility();
        Assert.False(bridge.Frames[^1].Collapsed);
        SetField(window, "_mayhemActive", false);
        window.MaintainOverlayVisibility();
        Assert.False(bridge.Enabled);
        });

    [Fact]
    public void NativeScanCommandRunsOnceAndKeepsDesktopHidden() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var bridge = new RecordingBridge();
        var scanner = new PendingScanner();
        using var window = new MayhemOverlayController(scanner, bridge) { GameBounds=()=>new Rectangle(0,0,1280,720) };
        SetField(window, "_tracking", true); SetField(window, "_mayhemActive", true);
        bridge.Command = 1;
        window.MaintainOverlayVisibility();
        bridge.Command = 1;
        window.MaintainOverlayVisibility();
        Assert.Equal(1, scanner.Calls);
        Assert.True(bridge.Frames[^1].Scanning);
        scanner.Completion.SetResult(new AugmentScanResult([], "测试识别结果"));
        Assert.True(SpinWait.SpinUntil(() =>
        {
            System.Windows.Forms.Application.DoEvents();
            window.MaintainOverlayVisibility();
            return !bridge.Frames[^1].Scanning;
        }, TimeSpan.FromSeconds(5)));
        Assert.Contains("测试识别结果", bridge.Frames[^1].Offered);
    });

    private static void SetField(MayhemOverlayController window, string name, bool value) =>
        typeof(MayhemOverlayController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);

    private sealed class RecordingBridge : IGameOverlayBridge
    {
        public List<GameOverlayFrame> Frames { get; } = [];
        public bool Enabled { get; private set; }
        public int Command { get; set; }
        public int Publish(GameOverlayFrame frame)
        {
            Enabled = true; Frames.Add(frame);
            int command = Command; Command = 0; return command;
        }
        public void Disable() => Enabled = false;
        public void Dispose() => Disable();
        public Task<GameOverlayCapture?> CaptureAsync(CancellationToken cancellationToken = default) => Task.FromResult<GameOverlayCapture?>(null);
    }

    private sealed class EmptyScanner : IAugmentScanner
    {
        public Task<AugmentScanResult> ScanAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AugmentScanResult([], "未识别"));
    }
    private sealed class PendingScanner : IAugmentScanner
    {
        public int Calls { get; private set; }
        public TaskCompletionSource<AugmentScanResult> Completion { get; } = new();
        public Task<AugmentScanResult> ScanAsync(CancellationToken cancellationToken = default) { Calls++; return Completion.Task; }
    }
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}
