namespace LOL_GameAssistant.Helper;

/// <summary>统一使用 AntdUI 提示气泡，管理延时、退出和控件释放。</summary>
internal sealed class UiToolTip : IDisposable
{
    private readonly Dictionary<Control, string> _texts = new();
    private readonly System.Windows.Forms.Timer _delay = new();
    private readonly System.Windows.Forms.Timer _dismiss = new();
    private Control? _active;
    private Form? _popup;
    private bool _disposed;
    public int InitialDelay { get; set; } = 350;
    public int ReshowDelay { get; set; } = 100;
    public int AutoPopDelay { get; set; } = 10000;
    public bool ShowAlways { get; set; }
    public int CustomWidth { get; set; } = 380;
    public event PopupEventHandler? Popup;

    public UiToolTip()
    {
        _delay.Tick += (_, _) => ShowPending();
        _dismiss.Tick += (_, _) => ClosePopup();
    }
    public string GetToolTip(Control control) => _texts.GetValueOrDefault(control, "");
    public void SetToolTip(Control control, string? text)
    {
        if (_disposed || control.IsDisposed) return;
        if (_texts.TryGetValue(control, out var previous) && previous == (text ?? "")) return;
        if (!_texts.ContainsKey(control))
        {
            control.MouseEnter += Enter; control.MouseLeave += Leave;
            control.MouseDown += MouseDown; control.Disposed += Removed;
        }
        _texts[control] = text ?? "";
        if (_active == control) { ClosePopup(); Schedule(control, ReshowDelay); }
        else if (control.IsHandleCreated && control.Visible && control.ClientRectangle.Contains(control.PointToClient(Control.MousePosition)))
            Schedule(control, InitialDelay);
    }
    private void Enter(object? sender, EventArgs e) { if (sender is Control c) Schedule(c, InitialDelay); }
    private void Leave(object? sender, EventArgs e) { if (sender == _active) Hide(_active!); }
    private void MouseDown(object? sender, MouseEventArgs e) { if (sender is Control c) Hide(c); }
    private void Removed(object? sender, EventArgs e)
    {
        if (sender is not Control c) return;
        Hide(c); Unhook(c); _texts.Remove(c);
    }
    private void Schedule(Control control, int delay)
    {
        _delay.Stop(); _active = control;
        if (string.IsNullOrEmpty(GetToolTip(control))) return;
        _delay.Interval = Math.Max(1, delay); _delay.Start();
    }
    private void ShowPending()
    {
        _delay.Stop();
        if (_active is not { IsDisposed: false, Visible: true } c || !c.IsHandleCreated) return;
        if (!ShowAlways && c.FindForm() is { ContainsFocus: false } form && Form.ActiveForm != form) return;
        string text = GetToolTip(c);
        if (string.IsNullOrEmpty(text)) return;
        var args = new PopupEventArgs(c.FindForm(), c, false, Size.Empty);
        Popup?.Invoke(this, args);
        if (args.Cancel) return;
        var palette = UiTheme.Palette;
        _popup = AntdUI.Tooltip.open(new AntdUI.Tooltip.Config(c, text)
        { CustomWidth = CustomWidth, Back = palette.SurfaceRaised, Fore = palette.TextPrimary });
        _dismiss.Interval = Math.Max(1, AutoPopDelay); _dismiss.Start();
    }
    public void Hide(Control control)
    {
        if (_active != control) return;
        _delay.Stop(); ClosePopup(); _active = null;
    }
    private void ClosePopup() { _dismiss.Stop(); _popup?.Close(); _popup?.Dispose(); _popup = null; }
    private void Unhook(Control c)
    { c.MouseEnter -= Enter; c.MouseLeave -= Leave; c.MouseDown -= MouseDown; c.Disposed -= Removed; }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _delay.Stop(); ClosePopup();
        foreach (var c in _texts.Keys) Unhook(c);
        _texts.Clear(); _delay.Dispose(); _dismiss.Dispose();
    }
}
