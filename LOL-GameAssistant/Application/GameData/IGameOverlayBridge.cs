namespace LOL_GameAssistant.Application.GameData;

/// <summary>游戏内覆盖层的数据与指令通道；不创建或激活桌面窗口。</summary>
public interface IGameOverlayBridge : IDisposable
{
    bool UseNativeOverlay => true;
    int Publish(GameOverlayFrame frame);
    void Disable();
    void PublishPanel(GameOverlayPanel panel) { }
    void HidePanel(GameOverlayPanelKind kind) { }
    GameOverlayPanelCommand? ReadPanelCommand(GameOverlayPanelKind kind) => null;
    Task<GameOverlayCapture?> CaptureAsync(CancellationToken cancellationToken = default);
}

public sealed record GameOverlayFrame(string Title, string Status, string Recommendations,
    string Offered, int Rarity, bool Collapsed, bool Scanning);

public enum GameOverlayPanelKind { Battle = 1, Coaching = 2, SkinNotice = 4, SkinPicker = 8 }

/// <summary>Image 由调用者持有，PublishPanel 在返回前复制像素；Version 防止旧列表点击选中另一款皮肤。</summary>
public sealed record GameOverlayPanel(GameOverlayPanelKind Kind, string Title = "", string Body = "", string Detail = "",
    Bitmap? Image = null, IReadOnlyList<string>? Items = null, int Selected = -1, bool Ready = true, bool CanRefresh = true,
    long Deadline = 0, int Position = 0, int OffsetX = 24, int OffsetY = 24, int Version = 0);

public sealed record GameOverlayPanelCommand(int Action, int Argument, int Version);

public sealed record GameOverlayCapture(Bitmap Image, Size GameSize) : IDisposable
{
    public void Dispose() => Image.Dispose();
}
