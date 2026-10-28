namespace LOL_GameAssistant.Helper;

internal static class UiContextMenu
{
    internal static void Open(Control owner, Point screenLocation, params (string Text, Action Run, bool Enabled)[] actions)
    {
        var items = actions.Select(a => (AntdUI.IContextMenuStripItem)new AntdUI.ContextMenuStripItem(UiLanguage.T(a.Text))
            { Enabled = a.Enabled, Tag = a.Run }).ToArray();
        AntdUI.ContextMenuStrip.open(new AntdUI.ContextMenuStrip.Config(owner, item =>
        {
            if (owner.IsDisposed || item.Tag is not Action run) return;
            if (owner.InvokeRequired) owner.BeginInvoke(run); else run();
        }, items) { Location = screenLocation, Font = owner.Font,
            ColorScheme = UiTheme.Palette.IsDark ? AntdUI.TAMode.Dark : AntdUI.TAMode.Light });
    }

    internal static void Bind(Control owner, params (string Text, Action Run, bool Enabled)[] actions)
    {
        owner.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) Open(owner, owner.PointToScreen(e.Location), actions); };
        owner.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Apps || e.KeyCode == Keys.F10 && e.Shift)
            { e.SuppressKeyPress = true; Open(owner, owner.PointToScreen(Point.Empty), actions); }
        };
    }
}
