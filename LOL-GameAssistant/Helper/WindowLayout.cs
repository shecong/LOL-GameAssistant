namespace LOL_GameAssistant.Helper;

internal static class WindowLayout
{
    private sealed record SizeLimits(Size Minimum, Size Maximum, int Dpi);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Form, SizeLimits> OriginalLimits = new();
    internal static void ConfigureDpi(ContainerControl control)
    {
        control.SuspendLayout();
        control.AutoScaleMode = AutoScaleMode.Dpi;
        control.AutoScaleDimensions = new SizeF(96, 96);
        control.ResumeLayout(false);
    }
    internal static void FitToWorkingArea(Form window)
    {
        if (window.IsDisposed || window.WindowState != FormWindowState.Normal) return;
        FitToWorkingArea(window, Screen.FromControl(window).WorkingArea, window.DeviceDpi);
    }

    internal static void PlaceForOpening(Form window)
    {
        if (window.IsDisposed || window.WindowState != FormWindowState.Normal) return;
        if (window.StartPosition != FormStartPosition.CenterParent) { FitToWorkingArea(window); return; }
        // WinForms only applies CenterParent automatically for modal dialogs.
        var owner = window.Owner;
        bool hasOwner = owner is { IsDisposed: false, Visible: true, WindowState: not FormWindowState.Minimized };
        Rectangle area = hasOwner ? Screen.FromControl(owner!).WorkingArea : Screen.FromControl(window).WorkingArea;
        FitToWorkingArea(window, area, window.DeviceDpi);
        Rectangle anchor = hasOwner ? owner!.Bounds : area;
        window.Location = new Point(anchor.Left + (anchor.Width - window.Width) / 2,
            anchor.Top + (anchor.Height - window.Height) / 2);
        FitToWorkingArea(window, area, window.DeviceDpi);
    }

    internal static void FitToWorkingArea(Form window, Rectangle area, int dpi)
    {
        if (window.IsDisposed || window.WindowState != FormWindowState.Normal) return;
        int margin = Math.Max(8, 16 * dpi / 96);
        area.Inflate(-margin, -margin);
        if (area.Width <= 0 || area.Height <= 0) return;
        var original = OriginalLimits.GetValue(window, value => new(value.MinimumSize, value.MaximumSize, dpi));
        // Restore the designed minimum when returning from a smaller monitor.
        window.MaximumSize = Size.Empty;
        window.MinimumSize = new Size(Math.Min(original.Minimum.Width * dpi / original.Dpi, area.Width),
            Math.Min(original.Minimum.Height * dpi / original.Dpi, area.Height));
        int maxWidth = original.Maximum.Width == 0 ? area.Width : Math.Min(area.Width, original.Maximum.Width * dpi / original.Dpi);
        int maxHeight = original.Maximum.Height == 0 ? area.Height : Math.Min(area.Height, original.Maximum.Height * dpi / original.Dpi);
        window.MaximumSize = new Size(maxWidth, maxHeight);
        var size = new Size(Math.Min(window.Width, area.Width), Math.Min(window.Height, area.Height));
        window.Bounds = new Rectangle(new Point(Math.Clamp(window.Left, area.Left, area.Right - size.Width),
            Math.Clamp(window.Top, area.Top, area.Bottom - size.Height)), size);
    }
}
