namespace LOL_GameAssistant.Domain.ApplicationInfo;

/// <summary>版本由 build/Version.props 统一编译进程序集，显示和更新比较使用同一值。</summary>
internal static class AppVersion
{
    public static Version Current => Normalize(typeof(AppVersion).Assembly.GetName().Version ?? new Version(1, 0, 0));

    public static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));

    public static bool IsNewer(Version remote, Version local) => Normalize(remote) > Normalize(local);
}
