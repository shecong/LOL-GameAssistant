using System.Security.Cryptography;
using System.Text.Json;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.Infrastructure.Lounge;

public sealed record LoungeSettings(string ServerUrl = "", bool ShareActivity = true,
    bool ShowVictories = true, string ProtectedToken = "", string[]? BlockedSenders = null,
    string[]? FollowedMembers = null, bool DoNotDisturb = false, bool QuietInGame = true,
    bool AnnouncePentakills = true, bool AnnounceLegendary = true, bool AnnouncePromotions = true)
{
    public const string DefaultServerUrl = "http://159.75.148.241:5000";
    public static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LOL-GameAssistant", "lounge.json");

    public static LoungeSettings Load()
    {
        LoungeSettings settings = new();
        try
        {
            if (File.Exists(SettingsPath)) settings = JsonSerializer.Deserialize<LoungeSettings>(File.ReadAllText(SettingsPath)) ?? settings;
            if (string.IsNullOrWhiteSpace(settings.ServerUrl))
            {
                string deployment = Path.Combine(AppContext.BaseDirectory, "lounge-server.txt");
                settings = settings with { ServerUrl = Environment.GetEnvironmentVariable("LOL_LOUNGE_SERVER") ??
                    (File.Exists(deployment) ? File.ReadAllText(deployment).Trim() : "") };
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        { RuntimeDiagnostics.WriteException(ex); }
        if (string.IsNullOrWhiteSpace(settings.ServerUrl)) settings = settings with { ServerUrl = DefaultServerUrl };
        return settings;
    }

    public LoungeSettings EnsureToken() => string.IsNullOrEmpty(SettingSecretProtector.Unprotect(ProtectedToken))
        ? this with { ProtectedToken = SettingSecretProtector.Protect(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))) }
        : this;

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        string temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, SettingsPath, true);
    }

    public static string ValidateUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            !(uri.Scheme == "https" || uri.Scheme == "http"))
            throw new ArgumentException("请输入 HTTP 或 HTTPS 服务器地址，不要包含用户名、密码、查询参数或片段。");
        return uri.AbsoluteUri.TrimEnd('/');
    }
}
