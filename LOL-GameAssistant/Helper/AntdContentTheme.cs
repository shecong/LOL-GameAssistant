namespace LOL_GameAssistant.Helper;

/// <summary>动态页面显式传递 AntdUI 主题，独立弹窗也不依赖主窗体的全局模式。</summary>
internal static class AntdContentTheme
{
    internal static void Apply(Control root, ThemePalette palette)
    {
        if (root is AntdUI.IControl control)
            control.ColorScheme = palette.IsDark ? AntdUI.TAMode.Dark : AntdUI.TAMode.Light;
        if (root is AntdUI.Input input)
        {
            input.BackColor = palette.SurfaceRaised;
            input.ForeColor = palette.TextPrimary;
            input.BorderColor = palette.Border;
            input.BorderHover = palette.Border;
            input.BorderActive = palette.Accent;
        }
        if (root is AntdUI.Table table)
        {
            table.BackColor = palette.SurfaceRaised;
            table.ForeColor = palette.TextPrimary;
            table.ColumnBack = palette.SurfaceMuted;
            table.ColumnFore = palette.TextSecondary;
            table.BorderColor = palette.Border;
        }
        foreach (Control child in root.Controls) Apply(child, palette);
    }
}

internal sealed class AntdContentWindow : AntdUI.Window, IThemeAware
{
    public void ApplyTheme(ThemePalette palette) => AntdContentTheme.Apply(this, palette);
}
