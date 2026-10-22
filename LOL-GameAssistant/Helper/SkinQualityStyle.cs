using LOL_GameAssistant.Domain.Skins;

namespace LOL_GameAssistant.Helper;

internal static class SkinQualityStyle
{
    public static string Label(SkinQuality quality) => UiLanguage.T(quality switch
    {
        SkinQuality.Base => "基础", SkinQuality.Standard => "普通", SkinQuality.Epic => "史诗",
        SkinQuality.Legendary => "传说", SkinQuality.Ultimate => "终极", SkinQuality.Mythic => "神话",
        SkinQuality.Exalted => "圣堂", SkinQuality.Transcendent => "卓越", _ => "品质未标注"
    });

    public static Color Foreground(SkinQuality quality, ThemePalette palette) => quality switch
    {
        // Higher tiers use stronger, warmer hues; avoid pale cyan/lavender for premium skins.
        SkinQuality.Epic => palette.IsDark ? Color.FromArgb(113, 152, 207) : Color.FromArgb(55, 102, 160),
        SkinQuality.Legendary => palette.IsDark ? Color.FromArgb(185, 145, 255) : Color.FromArgb(117, 65, 189),
        SkinQuality.Ultimate => palette.IsDark ? Color.FromArgb(255, 211, 74) : Color.FromArgb(151, 97, 0),
        SkinQuality.Mythic => palette.IsDark ? Color.FromArgb(255, 105, 212) : Color.FromArgb(181, 26, 131),
        SkinQuality.Exalted => palette.IsDark ? Color.FromArgb(255, 153, 64) : Color.FromArgb(179, 77, 6),
        SkinQuality.Transcendent => palette.IsDark ? Color.FromArgb(255, 100, 100) : Color.FromArgb(194, 35, 55),
        SkinQuality.Unknown => palette.TextSecondary,
        _ => palette.TextPrimary
    };
}
