using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>Stores picker state; interactive controls are drawn inside the game's DX11 frame.</summary>
internal sealed class SkinPickerOverlayController : IDisposable
{
    private readonly System.Windows.Forms.Timer _watch = new() { Interval = 100 };
    private readonly Func<bool> _available;
    private readonly Action<string> _select;
    private readonly Action _refresh, _restore;
    private readonly IGameOverlayBridge _overlay;
    private (string Id, string Name)[] _items = [];
    private string _title = "游戏换肤", _status = "";
    private string? _selected;
    private bool _ready, _canRefresh;
    private int _version;
    internal bool OverlayVisible { get; private set; }

    internal SkinPickerOverlayController(Func<bool> available, Action<string> select, Action refresh, Action restore,
        IGameOverlayBridge? overlay = null)
    {
        _available = available; _select = select; _refresh = refresh; _restore = restore;
        _overlay = overlay ?? AppCompositionRoot.GameOverlay;
        _watch.Tick += (_, _) =>
        {
            if (!_available() || !WindowHoldController.TryGetForegroundGameBounds(out _)) { Hide(); return; }
            PollCommands();
        };
    }
    internal void SetState((string Id, string Name)[] items, string? selected, string status, bool ready, bool canRefresh)
    {
        if (!_items.SequenceEqual(items)) { _items = items.ToArray(); ++_version; }
        _selected = selected; _status = status; _ready = ready; _canRefresh = canRefresh;
        PublishOverlay();
    }
    internal void ShowOverGame(Rectangle bounds)
    {
        OverlayVisible = true; _watch.Start(); PublishOverlay();
    }
    internal void SetHotkey(string hotkey) { _title = $"{hotkey} 显示 / 隐藏"; PublishOverlay(); }
    internal void PollCommands()
    {
        var command = _overlay.ReadPanelCommand(GameOverlayPanelKind.SkinPicker);
        if (command == null || !OverlayVisible || !_available() || command.Version != _version) return;
        switch (command.Action)
        {
            case 1 when _ready && command.Argument >= 0 && command.Argument < Math.Min(256, _items.Length): _select(_items[command.Argument].Id); break;
            case 2 when _canRefresh: _refresh(); break;
            case 3 when _ready: _restore(); break;
            case 4: Hide(); break;
        }
    }
    private void PublishOverlay()
    {
        if (!OverlayVisible) return;
        _overlay.PublishPanel(new(GameOverlayPanelKind.SkinPicker, _title, _status,
            Items: _items.Take(256).Select(p => p.Name).ToArray(), Selected: Array.FindIndex(_items, p => p.Id == _selected),
            Ready: _ready, CanRefresh: _canRefresh, Version: _version));
    }
    public void Hide()
    {
        OverlayVisible = false; _watch.Stop(); _overlay.HidePanel(GameOverlayPanelKind.SkinPicker);
    }
    public bool IsDisposed { get; private set; }
    public void Dispose()
    {
        if(IsDisposed) return;
        IsDisposed=true;
        Hide(); _watch.Dispose();
    }
}
