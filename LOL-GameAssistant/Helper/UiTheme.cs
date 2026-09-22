using LOL_GameAssistant.BaseViewForm;
using Microsoft.Win32;
using System.Runtime.CompilerServices;

namespace LOL_GameAssistant.Helper;

/// <summary>Application-level semantic colors and safe runtime theming for WinForms and custom controls.</summary>
public sealed record ThemePalette(
    bool IsDark,
    Color Surface,
    Color SurfaceRaised,
    Color SurfaceMuted,
    Color TextPrimary,
    Color TextSecondary,
    Color Border,
    Color Accent,
    Color BlueHeader,
    Color RedHeader);

public interface IThemeAware
{
    void ApplyTheme(ThemePalette palette);
}

public static class UiTheme
{
    private sealed record OriginalColors(Color BackColor, Color ForeColor);
    private sealed record OriginalGradient(Color StartColor, Color EndColor, Color BorderColor);

    private static readonly ConditionalWeakTable<Control, OriginalColors> Originals = new();
    private static readonly ConditionalWeakTable<GradientPanel, OriginalGradient> OriginalGradients = new();
    private static string _mode = "System";

    public static event EventHandler? Changed;

    public static ThemePalette Palette => CreatePalette(IsDark());
    public static string Mode => _mode;

    public static void SetMode(string? mode)
    {
        string normalized = mode is "Light" or "Dark" or "System" ? mode : "System";
        if (string.Equals(_mode, normalized, StringComparison.Ordinal)) return;
        _mode = normalized;
        RuntimeDiagnostics.Report("界面主题", normalized, IsDark() ? "深色主题已生效" : "浅色主题已生效");
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void Apply(Control root)
    {
        if (root.IsDisposed) return;
        ThemePalette palette = Palette;
        root.SuspendLayout();
        try
        {
            ApplyCore(root, palette);
            ApplyThemeAware(root, palette);
        }
        finally
        {
            root.ResumeLayout(true);
            root.Invalidate(true);
        }
    }

    private static void ApplyCore(Control control, ThemePalette palette)
    {
        OriginalColors original = Originals.GetValue(control, key => new OriginalColors(key.BackColor, key.ForeColor));
        if (!palette.IsDark)
        {
            control.BackColor = original.BackColor;
            control.ForeColor = original.ForeColor;
        }
        else
        {
            if (IsLightNeutral(original.BackColor))
                control.BackColor = IsNearlyWhite(original.BackColor) ? palette.SurfaceRaised : palette.Surface;

            if (IsDarkNeutral(original.ForeColor))
                control.ForeColor = original.ForeColor.GetBrightness() < .42f ? palette.TextPrimary : palette.TextSecondary;
        }

        if (control is GradientPanel gradient)
        {
            OriginalGradient originalGradient = OriginalGradients.GetValue(
                gradient,
                panel => new OriginalGradient(panel.StartColor, panel.EndColor, panel.BorderColor));
            if (!palette.IsDark)
            {
                gradient.StartColor = originalGradient.StartColor;
                gradient.EndColor = originalGradient.EndColor;
                gradient.BorderColor = originalGradient.BorderColor;
            }
            else if (IsLightSurface(originalGradient.StartColor) || IsLightSurface(originalGradient.EndColor))
            {
                gradient.StartColor = palette.SurfaceRaised;
                gradient.EndColor = palette.SurfaceMuted;
                if (originalGradient.BorderColor.A > 0) gradient.BorderColor = palette.Border;
            }
        }

        foreach (Control child in control.Controls)
            ApplyCore(child, palette);
    }

    private static void ApplyThemeAware(Control control, ThemePalette palette)
    {
        if (control is IThemeAware aware) aware.ApplyTheme(palette);
        foreach (Control child in control.Controls)
            ApplyThemeAware(child, palette);
    }

    private static bool IsDark() => _mode switch
    {
        "Dark" => true,
        "Light" => false,
        _ => IsSystemDark()
    };

    private static bool IsSystemDark()
    {
        try
        {
            object? value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1);
            return value is int number && number == 0;
        }
        catch
        {
            return false;
        }
    }

    private static ThemePalette CreatePalette(bool dark) => dark
        ? new ThemePalette(true,
            Color.FromArgb(18, 24, 33), Color.FromArgb(29, 38, 50), Color.FromArgb(42, 53, 67),
            Color.FromArgb(238, 242, 247), Color.FromArgb(184, 196, 210), Color.FromArgb(82, 98, 116),
            Color.FromArgb(100, 181, 246), Color.FromArgb(25, 118, 210), Color.FromArgb(198, 40, 40))
        : new ThemePalette(false,
            Color.FromArgb(240, 243, 248), Color.White, Color.FromArgb(246, 248, 251),
            Color.FromArgb(35, 45, 55), Color.FromArgb(88, 99, 110), Color.FromArgb(210, 218, 228),
            Color.FromArgb(25, 118, 210), Color.FromArgb(25, 118, 210), Color.FromArgb(198, 40, 40));

    private static bool IsLightNeutral(Color color) =>
        color.A > 0 && color.GetBrightness() > .76f && IsNeutral(color);

    private static bool IsNearlyWhite(Color color) => color.GetBrightness() > .93f && IsNeutral(color);

    private static bool IsDarkNeutral(Color color) =>
        color.A > 0 && color.GetBrightness() < .62f && IsNeutral(color);

    private static bool IsLightSurface(Color color) =>
        color.A > 0 && color.GetBrightness() > .76f &&
        Math.Max(color.R, Math.Max(color.G, color.B)) - Math.Min(color.R, Math.Min(color.G, color.B)) < 36;

    private static bool IsNeutral(Color color) =>
        Math.Max(color.R, Math.Max(color.G, color.B)) - Math.Min(color.R, Math.Min(color.G, color.B)) < 18;
}