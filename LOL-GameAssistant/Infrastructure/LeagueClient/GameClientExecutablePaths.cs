namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>客户端安装目录中的可执行文件选择规则。</summary>
internal static class GameClientExecutablePaths
{
    /// <summary>判断所选路径是否为支持的客户端启动程序。</summary>
    internal static bool IsSupportedExecutable(string path) =>
        string.Equals(Path.GetFileName(path), "LeagueClient.exe", StringComparison.OrdinalIgnoreCase) ||
        IsTencentLauncher(path);

    /// <summary>判断启动文件是否属于腾讯客户端入口。</summary>
    internal static bool IsTencentLauncher(string path) =>
        string.Equals(Path.GetFileName(path), "client.exe", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "TCLS", StringComparison.OrdinalIgnoreCase);

    /// <summary>在指定目录中直接查找支持的客户端入口。</summary>
    internal static string? FindDirectlyIn(string directory)
    {
        // 国服 TCLS 启动器与 LeagueClient 文件夹通常位于同一个游戏根目录。
        string tcls = Path.Combine(directory, "TCLS", "client.exe");
        if (File.Exists(tcls)) return tcls;

        if (string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(directory)), "TCLS", StringComparison.OrdinalIgnoreCase))
        {
            string client = Path.Combine(directory, "client.exe");
            if (File.Exists(client)) return client;
        }

        string league = Path.Combine(directory, "LeagueClient.exe");
        return File.Exists(league) ? league : null;
    }

    /// <summary>在多个候选入口中优先选取腾讯启动器。</summary>
    internal static string PreferTencentLauncher(string executable)
    {
        if (!string.Equals(Path.GetFileName(executable), "LeagueClient.exe", StringComparison.OrdinalIgnoreCase))
            return executable;

        string? leagueDirectory = Path.GetDirectoryName(executable);
        if (leagueDirectory == null) return executable;
        string adjacentTcls = Path.Combine(leagueDirectory, "TCLS", "client.exe");
        if (File.Exists(adjacentTcls)) return adjacentTcls;

        // 部分国服安装把 LeagueClient.exe 放在 LeagueClient 子目录中。
        if (!string.Equals(Path.GetFileName(leagueDirectory), "LeagueClient", StringComparison.OrdinalIgnoreCase))
            return executable;
        string? gameDirectory = Directory.GetParent(leagueDirectory)?.FullName;
        if (gameDirectory == null) return executable;

        string siblingTcls = Path.Combine(gameDirectory, "TCLS", "client.exe");
        return File.Exists(siblingTcls) ? siblingTcls : executable;
    }
}
