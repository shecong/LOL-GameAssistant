using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Domain.Skins;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>Stores picker state; interactive controls are drawn inside the game's DX11 frame.</summary>
internal sealed class SkinPickerOverlayController : IDisposable
{
    private readonly System.Windows.Forms.Timer _watch = new() { Interval = 100 };
    private readonly Func<bool> _available;
    private readonly Action<string> _select;
    private readonly Action _refresh, _restore;
    private readonly Action<SkinPickerCategory>? _changeCategory;
    private readonly Action<bool>? _changeCamera;
    private bool _cameraEnabled, _cameraReady;
    private readonly IGameOverlayBridge _overlay;
    private (string Id, string Name)[] _items = [];
    private GameOverlayItemStyle[] _styles = [];
    private string _title = "游戏换肤", _status = "";
    private string? _selected;
    private bool _ready, _canRefresh;
    private int _version;
    private SkinPickerCategory _category;
    private string _scope = "";
    private const int PageSize = 240;
    private int _page;
    private int PageCount => Math.Max(1, (_items.Length + PageSize - 1) / PageSize);
    internal bool OverlayVisible { get; private set; }

    internal SkinPickerOverlayController(Func<bool> available, Action<string> select, Action refresh, Action restore,
        IGameOverlayBridge? overlay = null, Action<SkinPickerCategory>? changeCategory = null, Action<bool>? changeCamera = null)
    {
        _available = available; _select = select; _refresh = refresh; _restore = restore;
        _changeCategory = changeCategory;
        _changeCamera = changeCamera;
        _overlay = overlay ?? AppCompositionRoot.GameOverlay;
        _watch.Tick += (_, _) =>
        {
            if (!_available() || !WindowHoldController.TryGetForegroundGameBounds(out _)) { Hide(); return; }
            PollCommands();
        };
    }
    internal void SetState((string Id, string Name)[] items, string? selected, string status, bool ready, bool canRefresh,
        SkinPickerCategory category = SkinPickerCategory.Hero, string scope = "", GameOverlayItemStyle[]? styles = null)
    {
        if (!_items.SequenceEqual(items) || !_styles.SequenceEqual(styles ?? []) || category != _category || scope != _scope)
        {
            _items = items.ToArray(); ++_version;
            _styles = styles?.ToArray() ?? [];
            int selectedIndex = Array.FindIndex(_items, item => item.Id == selected);
            _page = Math.Max(0, selectedIndex) / PageSize;
        }
        _category = category; _scope = scope;
        _selected = selected; _status = status; _ready = ready; _canRefresh = canRefresh;
        PublishOverlay();
    }
    internal void SetCameraState(bool enabled, bool ready)
    {
        _cameraEnabled = enabled; _cameraReady = ready; PublishOverlay();
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
            case 7 when _cameraReady && command.Argument is 0 or 1: _changeCamera?.Invoke(command.Argument == 1); break;
            case 1 when _ready && command.Argument >= 0 && command.Argument < Math.Min(PageSize, _items.Length - _page * PageSize):
                _select(_items[_page * PageSize + command.Argument].Id); break;
            case 2 when _canRefresh: _refresh(); break;
            case 3 when _ready: _restore(); break;
            case 4: Hide(); break;
            case 5 when _canRefresh && command.Argument is >= 0 and <= 2:
                if ((SkinPickerCategory)command.Argument != _category) _changeCategory?.Invoke((SkinPickerCategory)command.Argument);
                break;
            case 6 when _canRefresh && command.Argument is -1 or 1:
                int page = _page + command.Argument;
                if (page >= 0 && page < PageCount) { _page = page; ++_version; PublishOverlay(); }
                break;
        }
    }
    private void PublishOverlay()
    {
        if (!OverlayVisible) return;
        _overlay.PublishPanel(new(GameOverlayPanelKind.SkinPicker, _title, _status,
            Items: _items.Skip(_page * PageSize).Take(PageSize).Select(p => p.Name).ToArray(), Selected: Array.FindIndex(_items, p => p.Id == _selected) - _page * PageSize,
            Ready: _ready, CanRefresh: _canRefresh, Version: _version, Category: (int)_category, Page: _page, PageCount: PageCount,
            CameraEnabled: _cameraEnabled, CameraReady: _cameraReady,
            ItemStyles: _styles.Skip(_page * PageSize).Take(PageSize).ToArray()));
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
