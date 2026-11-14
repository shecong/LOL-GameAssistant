using System.Security.Cryptography;
using System.Text.Json;

namespace LOL_GameAssistant.Infrastructure.Pengu;

public sealed record PenguVisualSettings
{
    public bool Enabled { get; init; } = true;
    public bool ShowBadges { get; init; } = true;
    public bool ShowPlayerStats { get; init; } = true;
    public bool HideNavigation { get; init; }
    public string TierMode { get; init; } = "auto";
    public string WallpaperUrl { get; init; } = "";
    public string WallpaperName { get; init; } = "";
    public string Revision { get; init; } = "";
    public string RefreshNonce { get; init; } = "";
}

/// <summary>Settings live beside the plugin, so the client keeps them when the assistant exits.</summary>
public sealed class PenguVisualSettingsService(Func<string> pluginsDirectory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private string Target()
    {
        string target = Path.Combine(pluginsDirectory(), PenguPluginService.PluginId);
        for (DirectoryInfo? item = new(target); item != null; item = item.Parent)
            if (item.Exists && item.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("视觉插件目录不能使用目录链接。");
        return target;
    }
    public PenguVisualSettings Read()
    {
        string file = Path.Combine(Target(), "settings.json");
        if (!File.Exists(file)) return new();
        if (new FileInfo(file).Length > 65536) throw new InvalidOperationException("视觉设置文件过大。");
        return JsonSerializer.Deserialize<PenguVisualSettings>(File.ReadAllText(file), Json) ?? new();
    }
    public PenguVisualSettings Save(PenguVisualSettings settings)
    {
        if (!new[] { "auto", "ranked", "aram", "arena", "urf" }.Contains(settings.TierMode))
            throw new InvalidOperationException("请选择有效的英雄评级模式。");
        string url = settings.WallpaperUrl.Trim();
        if (url.Length > 4096) throw new InvalidOperationException("图片链接过长。");
        if (url.Length > 0)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                throw new InvalidOperationException("请输入 http/https 图片直链，或点击选择本地图片。");
            if ((uri.Host == "unsplash.com" || uri.Host.EndsWith(".unsplash.com")) && uri.AbsolutePath.StartsWith("/photos/"))
                throw new InvalidOperationException("这是 Unsplash 作品网页，请使用图片直链，或下载图片后选择本地图片。");
        }
        var saved = settings with { Revision = Guid.NewGuid().ToString("N"), WallpaperUrl = url };
        string target = Target();
        Directory.CreateDirectory(target);
        AtomicWrite(Path.Combine(target, "settings.json"), JsonSerializer.Serialize(saved, Json));
        return saved;
    }
    public PenguVisualSettings ImportWallpaper(string source, PenguVisualSettings settings)
    {
        if (new FileInfo(source).Length > 20 * 1024 * 1024) throw new InvalidOperationException("请选择不超过 20 MB 的图片。");
        // Decode and normalize to PNG, independent of the source path or extension.
        using var stream = File.OpenRead(source);
        using var image = System.Drawing.Image.FromStream(stream, false, true);
        if (image.Width > 12000 || image.Height > 12000 || (long)image.Width * image.Height > 40000000)
            throw new InvalidOperationException("图片尺寸过大，请先缩小图片。");
        using var output = new MemoryStream();
        double scale = Math.Min(1d, 2560d / Math.Max(image.Width, image.Height));
        using var normalized = new System.Drawing.Bitmap(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)));
        using (var graphics = System.Drawing.Graphics.FromImage(normalized))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(image, 0, 0, normalized.Width, normalized.Height);
        }
        normalized.Save(output, System.Drawing.Imaging.ImageFormat.Png);
        byte[] bytes = output.ToArray();
        string name = "wallpaper-" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + ".png";
        string target = Target();
        Directory.CreateDirectory(target);
        string destination = Path.Combine(target, name);
        if (!File.Exists(destination)) File.WriteAllBytes(destination, bytes);
        return settings with { WallpaperUrl = $"https://plugins/{PenguPluginService.PluginId}/{name}", WallpaperName = Path.GetFileName(source) };
    }
    private static void AtomicWrite(string file, string value)
    {
        if (File.Exists(file) && File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("设置文件不能使用文件链接。");
        string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, value); File.Move(temp, file, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
