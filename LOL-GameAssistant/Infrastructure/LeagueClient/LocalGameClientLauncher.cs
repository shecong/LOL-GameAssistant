using System.Diagnostics;
using System.Text.Json;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Domain.LeagueClient;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>
/// 本机文件系统和进程启动实现。
/// 用户只需选择安装文件夹，基础设施层负责扫描并启动 LeagueClient.exe。
/// </summary>
public sealed class LocalGameClientLauncher : IGameClientLauncher
{
    /// <inheritdoc />
    public string NormalizeConfiguredDirectory(string? configuredDirectory)
    {
        if (string.IsNullOrWhiteSpace(configuredDirectory)) return "";
        return File.Exists(configuredDirectory)
            ? Path.GetDirectoryName(configuredDirectory) ?? configuredDirectory
            : configuredDirectory.Trim();
    }

    /// <inheritdoc />
    public GameClientLaunchResult Start(string? configuredDirectory)
    {
        if (IsLeagueClientRunning())
            return new GameClientLaunchResult(true, "LOL 客户端已在运行。");

        string? executable = ResolveExecutable(configuredDirectory);
        if (executable == null)
            return new GameClientLaunchResult(false, "未找到 LeagueClient.exe，请在设置中选择 LOL 安装文件夹。");

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? AppDomain.CurrentDomain.BaseDirectory,
                UseShellExecute = true
            });
            return new GameClientLaunchResult(true, "已直接启动 LOL 客户端，等待登录和 LCU 连接。", executable);
        }
        catch (Exception ex)
        {
            return new GameClientLaunchResult(false, $"启动 LOL 客户端失败：{ex.Message}", executable);
        }
    }

    /// <summary>检测 Riot Client / League Client 是否已经在运行。</summary>
    private static bool IsLeagueClientRunning() =>
        Process.GetProcessesByName("LeagueClient").Length > 0 ||
        Process.GetProcessesByName("LeagueClientUx").Length > 0;

    /// <summary>优先扫描用户选择的文件夹，未命中时才检查常见安装目录。</summary>
    private static string? ResolveExecutable(string? configuredDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            // 兼容旧版本保存的 LeagueClient.exe 完整路径。
            if (File.Exists(configuredDirectory) &&
                string.Equals(Path.GetFileName(configuredDirectory), "LeagueClient.exe", StringComparison.OrdinalIgnoreCase))
                return configuredDirectory;

            if (Directory.Exists(configuredDirectory))
            {
                string? found = FindLeagueClientExecutable(configuredDirectory);
                if (found != null) return found;
            }
        }

        foreach (string candidate in GetKnownClientLocations())
        {
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>容错遍历安装目录，遇到无权限或失效目录时跳过而不中断启动流程。</summary>
    private static string? FindLeagueClientExecutable(string rootDirectory)
    {
        var pending = new Stack<string>();
        pending.Push(rootDirectory);

        while (pending.Count > 0)
        {
            string current = pending.Pop();
            try
            {
                string directCandidate = Path.Combine(current, "LeagueClient.exe");
                if (File.Exists(directCandidate)) return directCandidate;

                foreach (string child in Directory.EnumerateDirectories(current))
                    pending.Push(child);
            }
            catch (UnauthorizedAccessException)
            {
                // 用户选择了上层目录时，子目录可能无访问权限；继续扫描其它分支。
            }
            catch (IOException)
            {
                // 安装更新或目录被占用时继续扫描其它分支。
            }
        }
        return null;
    }

    private static IEnumerable<string> GetKnownClientLocations()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var candidates = new List<string>
        {
            Path.Combine(programFiles, "Riot Games", "League of Legends", "LeagueClient.exe"),
            Path.Combine(programFilesX86, "Riot Games", "League of Legends", "LeagueClient.exe"),
            Path.Combine(programFiles, "Tencent Games", "League of Legends", "LeagueClient.exe"),
            Path.Combine(programFilesX86, "Tencent Games", "League of Legends", "LeagueClient.exe")
        };

        // 国服常见于 WeGame 或非系统盘。只检查确定的安装相对路径，
        // 不递归扫描整块磁盘，避免启动助手时造成长时间卡顿。
        foreach (DriveInfo drive in DriveInfo.GetDrives().Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed))
        {
            string root = drive.RootDirectory.FullName;
            candidates.Add(Path.Combine(root, "Riot Games", "League of Legends", "LeagueClient.exe"));
            candidates.Add(Path.Combine(root, "Tencent Games", "League of Legends", "LeagueClient.exe"));
            candidates.Add(Path.Combine(root, "WeGameApps", "rail_apps", "LOL", "LeagueClient.exe"));
            candidates.Add(Path.Combine(root, "Program Files", "Riot Games", "League of Legends", "LeagueClient.exe"));
        }

        candidates.AddRange(GetRiotInstallManifestLocations());

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Riot Client 会把实际产品目录写入 ProgramData 的安装清单；这能覆盖自定义盘符、
    /// 非默认目录和客户端升级后的路径。读取失败时仅跳过，不影响手动配置路径。
    /// </summary>
    private static IEnumerable<string> GetRiotInstallManifestLocations()
    {
        var candidates = new List<string>();
        string manifest = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Riot Games",
            "RiotClientInstalls.json");
        if (!File.Exists(manifest)) return candidates;

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifest));
            foreach (string value in ReadJsonStrings(document.RootElement))
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (File.Exists(value) && string.Equals(Path.GetFileName(value), "LeagueClient.exe", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(value);
                    continue;
                }
                if (Directory.Exists(value))
                {
                    candidates.Add(Path.Combine(value, "LeagueClient.exe"));
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (JsonException)
        {
        }
        return candidates;
    }

    private static IEnumerable<string> ReadJsonStrings(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                string? value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value)) yield return value;
                yield break;
            case JsonValueKind.Array:
                foreach (JsonElement child in element.EnumerateArray())
                foreach (string item in ReadJsonStrings(child))
                    yield return item;
                yield break;
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                foreach (string item in ReadJsonStrings(property.Value))
                    yield return item;
                yield break;
            default:
                yield break;
        }
    }
}
