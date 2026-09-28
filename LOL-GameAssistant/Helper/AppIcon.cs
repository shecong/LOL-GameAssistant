using System.Drawing;

namespace LOL_GameAssistant.Helper;

/// <summary>与 EXE 使用同一份 ico 的窗口和托盘图标。</summary>
internal static class AppIcon
{
    private const string ResourceName = "LOL_GameAssistant.AppIcon.ico";

    public static Icon Shared { get; } = Load();

    private static Icon Load()
    {
        using Stream stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"未找到内嵌程序图标：{ResourceName}");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }
}
