namespace LOL_GameAssistant.Domain.ApplicationInfo;

/// <summary>统一显示和比较发布版本，缺省的版本段按零处理。</summary>
internal static class AppVersion
{
    public static Version Current => Normalize(typeof(AppVersion).Assembly.GetName().Version ?? new Version(1, 0, 0));

    public static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));

    public static bool IsNewer(Version remote, Version local) => Normalize(remote) > Normalize(local);
}
