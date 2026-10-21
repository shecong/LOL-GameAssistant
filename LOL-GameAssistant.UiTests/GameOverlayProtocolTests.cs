using System.IO.MemoryMappedFiles;
using System.Diagnostics;
using System.Text;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Infrastructure.GameData;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class GameOverlayProtocolTests
{
    [Fact]
    public void ExitDisablesAllPanelsBeforeClosingMappingAndCannotRestartHeartbeat()
    {
        using var bridge = new NativeGameOverlayBridge(connectToGame:false);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        var sessionType = typeof(NativeGameOverlayBridge).GetNestedType("Session", System.Reflection.BindingFlags.NonPublic)!;
        string name = $"Local\\SC.Overlay.Exit.Test.{Guid.NewGuid():N}";
        object session = Activator.CreateInstance(sessionType,flags,null,[name,Environment.ProcessId],null)!;
        typeof(NativeGameOverlayBridge).GetField("_session",flags)!.SetValue(bridge,session);
        using var observer = MemoryMappedFile.OpenExisting(name);
        using var view = observer.CreateViewAccessor();
        bridge.PublishPanel(new(GameOverlayPanelKind.SkinPicker,Items:["基础皮肤"]));
        Assert.Equal(1,view.ReadInt32(8));
        bridge.Dispose();
        Assert.Equal(0,view.ReadInt32(8));
        bridge.Publish(new("", "", "", "",0,false,false));
        bridge.PublishPanel(new(GameOverlayPanelKind.Coaching,Body:"旧回调"));
        typeof(NativeGameOverlayBridge).GetMethod("Heartbeat",flags)!.Invoke(bridge,null);
        Assert.Equal(0,view.ReadInt32(8));
    }

    [Fact]
    public void AuxiliaryPanelsKeepHeartbeatAndPixelsWhenMayhemIsDisabledAndPreserveCommands()
    {
        using var bridge = new NativeGameOverlayBridge(connectToGame: false);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        var sessionType = typeof(NativeGameOverlayBridge).GetNestedType("Session", System.Reflection.BindingFlags.NonPublic)!;
        object session = Activator.CreateInstance(sessionType, flags, null, [$"Local\\SC.Overlay.Test.{Guid.NewGuid():N}", Environment.ProcessId], null)!;
        typeof(NativeGameOverlayBridge).GetField("_session", flags)!.SetValue(bridge, session);
        var view = (MemoryMappedViewAccessor)sessionType.GetProperty("View")!.GetValue(session)!;
        const int aux = NativeGameOverlayBridge.AuxOffset;
        Assert.Equal(2, view.ReadInt32(4));
        bridge.Publish(new("增幅", "", "", "", 0, false, false));
        bridge.PublishPanel(new(GameOverlayPanelKind.Coaching, "建议", "控龙", Deadline: Environment.TickCount64 + 10000));
        using (var image = new Bitmap(2,3))
        {
            using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.Red);
            bridge.PublishPanel(new(GameOverlayPanelKind.Battle, Image: image));
        }
        view.Write(aux+64,1); view.Write(aux+68,2); view.Write(aux+72,7);
        bridge.PublishPanel(new(GameOverlayPanelKind.SkinPicker, Items: ["基础", "大元素使"], Selected: 1, Version: 8, Category: 2, Page: 1, PageCount: 3));
        bridge.Disable();
        Assert.Equal(1,view.ReadInt32(8)); Assert.Equal(0,view.ReadInt32(80));
        Assert.Equal(1|2|8,view.ReadInt32(aux));
        Assert.Equal(2,view.ReadInt32(aux+8)); Assert.Equal(3,view.ReadInt32(aux+12));
        Assert.Equal((byte)255,view.ReadByte(NativeGameOverlayBridge.BattlePixelOffset+2));
        Assert.Equal("大元素使",ReadText(view,aux+6784+192,192));
        Assert.Equal(1,view.ReadInt32(aux+52)); Assert.Equal(8,view.ReadInt32(aux+60));
        Assert.Equal(2,view.ReadInt32(aux+76)); Assert.Equal(1,view.ReadInt32(aux+80)); Assert.Equal(3,view.ReadInt32(aux+84));
        Assert.Equal(new GameOverlayPanelCommand(1,2,7),bridge.ReadPanelCommand(GameOverlayPanelKind.SkinPicker));
        Assert.Null(bridge.ReadPanelCommand(GameOverlayPanelKind.SkinPicker));
        bridge.HidePanel(GameOverlayPanelKind.Coaching); bridge.HidePanel(GameOverlayPanelKind.SkinPicker);
        Assert.Equal(1,view.ReadInt32(aux)); Assert.Equal(1,view.ReadInt32(8));
        bridge.HidePanel(GameOverlayPanelKind.Battle); Assert.Equal(0,view.ReadInt32(8));
    }

    [Fact]
    public async Task ProductionLoaderRejectsNonGameProcessBeforeLoadingModule()
    {
        string host = Path.Combine(AppContext.BaseDirectory, "Tools", "GameOverlay", "sc-game-overlay-host.exe");
        Assert.True(File.Exists(host));
        var start = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        start.ArgumentList.Add(Environment.ProcessId.ToString()); start.ArgumentList.Add(Environment.ProcessId.ToString()); start.ArgumentList.Add("1");
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        string output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        Assert.Equal(4, process.ExitCode);
        Assert.Contains("target_mismatch", output);
    }

    [Fact]
    public void FrameMatchesNativeOffsetsAndPreservesRenderAndCaptureResponses()
    {
        using var mapping = MemoryMappedFile.CreateNew(null, NativeGameOverlayBridge.PixelOffset);
        using var view = mapping.CreateViewAccessor();
        view.Write(0, 0x53434f56u); view.Write(12, 2); view.Write(32, 12); view.Write(48, 7); view.Write(52, 7);
        NativeGameOverlayBridge.WriteFrame(view, new GameOverlayFrame("海克斯", "当前英雄", "双刀流", "本次识别", 2, true, true), true);
        Assert.Equal(0x53434f56u, view.ReadUInt32(0));
        Assert.Equal(1, view.ReadInt32(8)); Assert.Equal(2, view.ReadInt32(12));
        Assert.Equal(12, view.ReadInt32(32)); Assert.Equal(7, view.ReadInt32(48)); Assert.Equal(7, view.ReadInt32(52));
        Assert.InRange(view.ReadInt64(24), Environment.TickCount64 - 5000, Environment.TickCount64);
        Assert.Equal(2, view.ReadInt32(36)); Assert.Equal(1, view.ReadInt32(40)); Assert.Equal(1, view.ReadInt32(44));
        Assert.Equal("海克斯", ReadText(view, 128, 256));
        Assert.Equal("当前英雄", ReadText(view, 384, 2048));
        Assert.Equal("双刀流", ReadText(view, 2432, 32768));
        Assert.Equal("本次识别", ReadText(view, 35200, 16384));
    }

    [Fact]
    public void TextTruncatesAtUtf8BoundaryAndClearsOldContent()
    {
        using var mapping = MemoryMappedFile.CreateNew(null, 1024);
        using var view = mapping.CreateViewAccessor();
        NativeGameOverlayBridge.WriteText(view, 0, 256, string.Concat(Enumerable.Repeat("棱彩🧪", 100)));
        Assert.NotEmpty(ReadText(view, 0, 256));
        Assert.Equal((byte)0, view.ReadByte(255));
        NativeGameOverlayBridge.WriteText(view, 0, 256, "黄金");
        Assert.Equal("黄金", ReadText(view, 0, 256));
        var tail = new byte[250]; view.ReadArray(6, tail, 0, tail.Length);
        Assert.All(tail, value => Assert.Equal((byte)0, value));
    }

    private static string ReadText(MemoryMappedViewAccessor view, int offset, int size)
    {
        var bytes = new byte[size]; view.ReadArray(offset, bytes, 0, size);
        return new UTF8Encoding(false, true).GetString(bytes).TrimEnd('\0');
    }
}
