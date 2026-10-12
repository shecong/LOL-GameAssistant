namespace LOL_GameAssistant.Helper;

internal static class WindowLayout
{
    private sealed record Maximum(Size Size, int Dpi);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Form, Maximum> OriginalMaximums = new();
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
        Rectangle area = Screen.FromControl(window).WorkingArea;
        int margin = Math.Max(8, 16 * window.DeviceDpi / 96);
        area.Inflate(-margin, -margin);
        if (area.Width <= 0 || area.Height <= 0) return;
        window.MinimumSize = new Size(Math.Min(window.MinimumSize.Width, area.Width), Math.Min(window.MinimumSize.Height, area.Height));
        var original = OriginalMaximums.GetValue(window, value => new(value.MaximumSize, value.DeviceDpi));
        int maxWidth = original.Size.Width == 0 ? area.Width : Math.Min(area.Width, original.Size.Width * window.DeviceDpi / original.Dpi);
        int maxHeight = original.Size.Height == 0 ? area.Height : Math.Min(area.Height, original.Size.Height * window.DeviceDpi / original.Dpi);
        window.MaximumSize = new Size(maxWidth, maxHeight);
        var size = new Size(Math.Min(window.Width, area.Width), Math.Min(window.Height, area.Height));
        window.Bounds = new Rectangle(new Point(Math.Clamp(window.Left, area.Left, area.Right - size.Width),
            Math.Clamp(window.Top, area.Top, area.Bottom - size.Height)), size);
    }
}
