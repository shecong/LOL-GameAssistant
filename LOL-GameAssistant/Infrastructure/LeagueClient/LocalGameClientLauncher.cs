using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Helper;
using System.Diagnostics;
using System.Text.Json;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>
/// 本机文件系统和进程启动实现。
/// 用户只需选择安装文件夹，基础设施层负责扫描并启动 LeagueClient.exe。
/// </summary>
public sealed class LocalGameClientLauncher : IGameClientLauncher
{
    private static readonly TimeSpan LaunchVerificationTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LaunchVerificationPollInterval = TimeSpan.FromMilliseconds(750);

    /// <inheritdoc />
    public string NormalizeConfiguredDirectory(string? configuredDirectory)
    {
        if (string.IsNullOrWhiteSpace(configuredDirectory)) return "";
        string path = configuredDirectory.Trim().Trim('"');
        return File.Exists(path)
            ? Path.GetDirectoryName(path) ?? path
            : path;
    }

    /// <inheritdoc />
    public GameClientLaunchResult Start(string? configuredDirectory)
    {
        ClientState state = GetClientState();
        if (state.WindowVisible)
            return new GameClientLaunchResult(true, "LOL 客户端已在运行并显示窗口。", null, state.LcuReady, true);

        if (state.ProcessRunning)
            return new GameClientLaunchResult(true, "检测到 LOL 后台进程，正在等待客户端主窗口和 LCU 就绪。", null, state.LcuReady, false);

        string? executable = ResolveExecutable(configuredDirectory);
        if (executable == null)
            return new GameClientLaunchResult(false, string.IsNullOrWhiteSpace(configuredDirectory)
                ? "未找到 LeagueClient.exe，请选择 LOL 安装文件夹。"
                : $"所选位置没有 LeagueClient.exe：{configuredDirectory}。请选择游戏安装目录或 LeagueClient 文件夹。");

        try
        {
            LaunchTarget target = ResolveLaunchTarget(executable);
            Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = target.Executable,
                Arguments = target.Arguments,
                WorkingDirectory = Path.GetDirectoryName(target.Executable) ?? AppDomain.CurrentDomain.BaseDirectory,
                UseShellExecute = true
            });
            if (process == null)
                return new GameClientLaunchResult(false, "Windows 未返回客户端启动进程。", executable);

            RuntimeDiagnostics.Report("LOL 客户端", "启动中", $"已请求启动 {Path.GetFileName(target.Executable)}，等待主窗口与 LCU");
            return new GameClientLaunchResult(true, $"已启动 {Path.GetFileName(target.Executable)}，正在等待 LOL 客户端。", executable);
        }
        catch (Exception ex)
        {
            RuntimeDiagnostics.Report("LOL 客户端", "启动失败", ex.Message);
            return new GameClientLaunchResult(false, $"启动 LOL 客户端失败：{ex.Message}", executable);
        }
    }

    public async Task<GameClientLaunchResult> StartAndVerifyAsync(
        string? configuredDirectory,
        CancellationToken cancellationToken = default)
    {
        // 安装目录扫描和进程启动可能较慢；设置页按钮需要立即显示“启动中”。
        GameClientLaunchResult started = await Task.Run(() => Start(configuredDirectory), cancellationToken).ConfigureAwait(false);
        if (!started.Started) return started;

        DateTime deadline = DateTime.UtcNow + LaunchVerificationTimeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClientState state = GetClientState(started.ExecutablePath);
            if (state.LcuReady)
            {
                string executable = started.ExecutablePath ?? NormalizeConfiguredDirectory(configuredDirectory);
                RuntimeDiagnostics.Report("LOL 客户端", "已就绪", "LCU lockfile 已读取");
                return new GameClientLaunchResult(true, "LOL 客户端已启动，LCU 已就绪。", executable, true, state.WindowVisible);
            }

            await Task.Delay(LaunchVerificationPollInterval, cancellationToken).ConfigureAwait(false);
        }

        ClientState finalState = GetClientState(started.ExecutablePath);
        if (finalState.ProcessRunning || finalState.LauncherRunning)
        {
            const string waiting = "启动器或 LOL 客户端已运行，正在等待登录或更新；登录完成后助手会自动连接。";
            RuntimeDiagnostics.Report("LOL 客户端", "等待登录", waiting);
            return new GameClientLaunchResult(true, waiting, started.ExecutablePath, false, finalState.WindowVisible);
        }

        const string failed = "启动器进程已退出，未检测到 LOL 客户端。请检查安装路径或通过原启动器完成更新。";
        RuntimeDiagnostics.Report("LOL 客户端", "未就绪", failed);
        return new GameClientLaunchResult(false, failed, started.ExecutablePath);
    }

    private static ClientState GetClientState(string? leagueClientExecutable = null)
    {
        Process[] processes = Process.GetProcessesByName("LeagueClientUx")
            .Concat(Process.GetProcessesByName("LeagueClient"))
            .ToArray();
        try
        {
            bool visible = processes.Any(process =>
            {
                try
                {
                    process.Refresh();
                    return !process.HasExited && process.MainWindowHandle != IntPtr.Zero;
                }
                catch
                {
                    return false;
                }
            });
            bool lcuReady = processes.Length > 0 && HasReadableLcuLockfile(processes, leagueClientExecutable);
            Process[] launchers = Process.GetProcessesByName("RiotClientServices");
            bool launcherRunning = launchers.Length > 0;
            foreach (Process launcher in launchers) launcher.Dispose();
            return new ClientState(processes.Length > 0, visible, lcuReady, launcherRunning);
        }
        finally
        {
            foreach (Process process in processes) process.Dispose();
        }
    }

    private static bool HasReadableLcuLockfile(IEnumerable<Process> processes, string? leagueClientExecutable)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Process process in processes)
        {
            try
            {
                string? executable = process.MainModule?.FileName;
                string? directory = string.IsNullOrWhiteSpace(executable) ? null : Path.GetDirectoryName(executable);
                if (!string.IsNullOrWhiteSpace(directory)) directories.Add(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // 进程可能刚退出或权限不同，尝试已解析安装目录中的 lockfile。
            }
        }
        if (!string.IsNullOrWhiteSpace(leagueClientExecutable))
        {
            string? configuredDirectory = Path.GetDirectoryName(leagueClientExecutable);
            if (!string.IsNullOrWhiteSpace(configuredDirectory)) directories.Add(configuredDirectory);
        }

        foreach (string directory in directories)
        {
            try
            {
                string lockfile = Path.Combine(directory, "lockfile");
                if (!File.Exists(lockfile)) continue;
                string[] parts = File.ReadAllText(lockfile).Trim().Split(':');
                if (parts.Length >= 4 && int.TryParse(parts[1], out _) && !string.IsNullOrWhiteSpace(parts[2])) return true;
            }
            catch (IOException)
            {
                // The client may be replacing the lockfile while it starts; retry on the next poll.
            }
            catch (UnauthorizedAccessException)
            {
                // A differently elevated client cannot be verified from this process.
            }
            catch (InvalidOperationException)
            {
                // The process exited while being inspected.
            }
        }
        return false;
    }

    private static LaunchTarget ResolveLaunchTarget(string leagueClientExecutable)
    {
        string? leagueDirectory = Path.GetDirectoryName(leagueClientExecutable);
        string? installDirectory = leagueDirectory == null ? null : Directory.GetParent(leagueDirectory)?.FullName;
        string? candidate = installDirectory == null ? null : Path.Combine(installDirectory, "Riot Client", "RiotClientServices.exe");
        if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
        {
            string? patchline = ResolvePatchline(candidate);
            if (!string.IsNullOrWhiteSpace(patchline))
                return new LaunchTarget(candidate, $"--launch-product=league_of_legends --launch-patchline={patchline}");

            // 国服分支名称由 Riot 安装清单给出；缺少清单时不能误用国际服 live。
            if (!IsTencentInstall(leagueClientExecutable))
                return new LaunchTarget(candidate, "--launch-product=league_of_legends --launch-patchline=live");
        }
        return new LaunchTarget(leagueClientExecutable, "");
    }

    private sealed record LaunchTarget(string Executable, string Arguments);
    private sealed record ClientState(bool ProcessRunning, bool WindowVisible, bool LcuReady, bool LauncherRunning);

    private static bool IsTencentInstall(string executablePath) =>
        executablePath.Contains("WeGameApps", StringComparison.OrdinalIgnoreCase) ||
        executablePath.Contains("Tencent Games", StringComparison.OrdinalIgnoreCase) ||
        executablePath.Contains("英雄联盟", StringComparison.OrdinalIgnoreCase);

    /// <summary>从 Riot 自己的安装清单匹配服务程序，避免把国服误当成国际服 live 分支。</summary>
    private static string? ResolvePatchline(string riotClientExecutable)
    {
        string manifest = GetRiotInstallManifestPath();
        if (!File.Exists(manifest)) return null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifest));
            if (!document.RootElement.TryGetProperty("patchlines", out JsonElement patchlines) ||
                patchlines.ValueKind != JsonValueKind.Object)
                return null;

            string normalizedTarget = Path.GetFullPath(riotClientExecutable);
            foreach (JsonProperty patchline in patchlines.EnumerateObject())
            {
                if (patchline.Value.ValueKind != JsonValueKind.String) continue;
                string? servicePath = patchline.Value.GetString();
                if (string.IsNullOrWhiteSpace(servicePath)) continue;
                if (string.Equals(Path.GetFullPath(servicePath), normalizedTarget, StringComparison.OrdinalIgnoreCase))
                    return patchline.Name;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            // 安装清单不可用时回退到可直接运行的客户端程序。
        }
        return null;
    }

    /// <summary>优先扫描用户选择的文件夹，未命中时才检查常见安装目录。</summary>
    private static string? ResolveExecutable(string? configuredDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            configuredDirectory = configuredDirectory.Trim().Trim('"');
            // 兼容旧版本保存的 LeagueClient.exe 完整路径。
            if (File.Exists(configuredDirectory) &&
                string.Equals(Path.GetFileName(configuredDirectory), "LeagueClient.exe", StringComparison.OrdinalIgnoreCase))
                return configuredDirectory;

            if (Directory.Exists(configuredDirectory))
            {
                string? found = FindLeagueClientExecutable(configuredDirectory);
                if (found != null) return found;
            }
            // 用户指定安装位置时不偷偷启动其它安装版本。
            return null;
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
        const int maximumDirectories = 4000;
        int visitedDirectories = 0;

        while (pending.Count > 0 && visitedDirectories++ < maximumDirectories)
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
        var candidates = new List<string>();
        candidates.AddRange(GetRiotInstallManifestLocations());
        candidates.AddRange(new[]
        {
            Path.Combine(programFiles, "Riot Games", "League of Legends", "LeagueClient.exe"),
            Path.Combine(programFilesX86, "Riot Games", "League of Legends", "LeagueClient.exe"),
            Path.Combine(programFiles, "Tencent Games", "League of Legends", "LeagueClient.exe"),
            Path.Combine(programFilesX86, "Tencent Games", "League of Legends", "LeagueClient.exe")
        });

        // 国服常见于 WeGame 或非系统盘。只检查确定的安装相对路径，
        // 不递归扫描整块磁盘，避免启动助手时造成长时间卡顿。
        foreach (DriveInfo drive in DriveInfo.GetDrives().Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed))
        {
            string root = drive.RootDirectory.FullName;
            candidates.Add(Path.Combine(root, "Riot Games", "League of Legends", "LeagueClient.exe"));
            candidates.Add(Path.Combine(root, "Tencent Games", "League of Legends", "LeagueClient.exe"));
            candidates.Add(Path.Combine(root, "WeGameApps", "rail_apps", "LOL", "LeagueClient.exe"));
            candidates.Add(Path.Combine(root, "WeGameApps", "英雄联盟（含经典模式）", "LeagueClient", "LeagueClient.exe"));
            candidates.Add(Path.Combine(root, "WeGameApps", "英雄联盟", "LeagueClient", "LeagueClient.exe"));
            candidates.Add(Path.Combine(root, "Program Files", "Riot Games", "League of Legends", "LeagueClient.exe"));
        }

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Riot Client 会把实际产品目录写入 ProgramData 的安装清单；这能覆盖自定义盘符、
    /// 非默认目录和客户端升级后的路径。读取失败时仅跳过，不影响手动配置路径。
    /// </summary>
    private static IEnumerable<string> GetRiotInstallManifestLocations()
    {
        var candidates = new List<string>();
        string manifest = GetRiotInstallManifestPath();
        if (!File.Exists(manifest)) return candidates;

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifest));
            // associated_client 的安装目录在 JSON 属性名中，旧实现只读属性值会漏掉国服。
            if (document.RootElement.TryGetProperty("associated_client", out JsonElement associations) &&
                associations.ValueKind == JsonValueKind.Object)
            {
                string? defaultService = document.RootElement.TryGetProperty("rc_default", out JsonElement defaultElement) &&
                    defaultElement.ValueKind == JsonValueKind.String
                    ? defaultElement.GetString()
                    : null;
                var preferred = new List<string>();
                var others = new List<string>();
                foreach (JsonProperty association in associations.EnumerateObject())
                {
                    try
                    {
                        string directory = Path.GetFullPath(association.Name.Replace('/', Path.DirectorySeparatorChar));
                        string executable = Path.Combine(directory, "LeagueClient.exe");
                        string? service = association.Value.ValueKind == JsonValueKind.String
                            ? association.Value.GetString()
                            : null;
                        if (!string.IsNullOrWhiteSpace(defaultService) && !string.IsNullOrWhiteSpace(service) &&
                            string.Equals(Path.GetFullPath(defaultService), Path.GetFullPath(service), StringComparison.OrdinalIgnoreCase))
                            preferred.Add(executable);
                        else
                            others.Add(executable);
                    }
                    catch (ArgumentException) { }
                }
                candidates.AddRange(preferred);
                candidates.AddRange(others);
            }
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

    private static string GetRiotInstallManifestPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Riot Games",
        "RiotClientInstalls.json");

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