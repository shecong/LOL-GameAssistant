using System.Text.Json;
using System.Diagnostics;
using LOL_GameAssistant.Application.ClientFeatures;

namespace LOL_GameAssistant.Infrastructure.Pengu;

/// <summary>仅管理本插件的已知文件；其他插件与用户文件均保留。</summary>
public sealed record PenguClientRuntime(bool ClientRunning, string? LoaderDirectory, string? Error = null);

public sealed class PenguPluginService(string bundleDirectory, string settingsFile,
    Func<string, PenguClientRuntime>? runtimeProbe = null,
    Func<string, PenguLoaderActivation>? activationProbe = null,
    Func<string>? bundleProvider = null) : IPenguPluginService
{
    public const string PluginId = "lol-game-assistant-visuals";
    private static readonly string[] Payload = ["index.js", "pengu.yml", "README.md"];
    private static readonly string[] ManagedFiles = [.. Payload, "index.js.disabled", "runtime.json"];
    public string PluginsDirectory { get; private set; } = ReadDirectory(settingsFile);

    public void SelectDirectory(string directory)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory.Trim().Trim('"')));
        if (!Path.GetFileName(root).Equals("plugins", StringComparison.OrdinalIgnoreCase) &&
            (File.Exists(Path.Combine(root, "core.dll")) || Directory.Exists(Path.Combine(root, "plugins")))) root = Path.Combine(root, "plugins");
        if (!Directory.Exists(root)) throw new InvalidOperationException("请选择 Pengu Loader 下已存在的 plugins 文件夹，或包含它的 Loader 目录。");
        EnsureRegularPath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(settingsFile))!);
        AtomicWrite(settingsFile, JsonSerializer.Serialize(root));
        PluginsDirectory = root;
    }

    public PenguPluginStatus Inspect()
    {
        if (string.IsNullOrEmpty(PluginsDirectory)) return new(false, false, false, "请选择 Pengu Loader 的 plugins 目录。");
        string target = Target();
        if (!Directory.Exists(target)) return new(false, false, false, "尚未安装客户端视觉插件。");
        bool enabled = File.Exists(Path.Combine(target, "index.js"));
        bool installed = enabled || File.Exists(Path.Combine(target, "index.js.disabled"));
        if (!installed) return new(false, false, false, "尚未安装客户端视觉插件。");
        VerifyOwned(target);
        string runtime = Path.Combine(target, "runtime.json");
        if (File.Exists(runtime) && new FileInfo(runtime).Length <= 16384)
        {
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(runtime));
                var state = json.RootElement;
                if (state.ValueKind == JsonValueKind.Object && state.TryGetProperty("timestamp", out var stamp) &&
                    stamp.ValueKind == JsonValueKind.String && stamp.TryGetDateTimeOffset(out var timestamp) &&
                    DateTimeOffset.UtcNow - timestamp is var age && age >= TimeSpan.FromSeconds(-5) && age < TimeSpan.FromSeconds(35) &&
                    state.TryGetProperty("active", out var active) && active.ValueKind == JsonValueKind.True)
                {
                    string summary = state.TryGetProperty("managed", out var managed) && managed.ValueKind == JsonValueKind.True
                        ? "客户端已连接 sc助手控制" : state.TryGetProperty("panelMounted", out var panel) && panel.ValueKind == JsonValueKind.True
                        ? "客户端面板已挂载" : "客户端已加载，面板尚未挂载";
                    string applied = "";
                    if (state.TryGetProperty("managed", out var controlled) && controlled.ValueKind == JsonValueKind.True)
                    {
                        string settings = Path.Combine(target, "settings.json");
                        try
                        {
                            if (File.Exists(settings) && new FileInfo(settings).Length <= 65536)
                            {
                                using var desired = JsonDocument.Parse(File.ReadAllText(settings));
                                applied = desired.RootElement.TryGetProperty("revision", out var expected) &&
                                    state.TryGetProperty("revision", out var actual) && expected.GetString() == actual.GetString()
                                    ? " · 设置已应用" : " · 设置等待客户端应用";
                            }
                        }
                        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
                        { applied = " · 设置文件读取失败"; }
                    }
                    string Detail(string key) => state.TryGetProperty(key, out var value) ? value.ToString() : "-";
                    return new(installed, enabled, true, $"{summary}{applied} · v{Detail("version")} · 模式 {Detail("mode")} · 头像 {Detail("targets")} / 角标 {Detail("badges")}\n{Detail("error")} {Detail("configError")} {Detail("wallpaper")}" +
                        (enabled ? "" : "\n下次启动已停用；当前客户端仍在运行，请重启客户端。"));
                }
            }
            catch (JsonException) { /* A stale or incomplete diagnostic is not proof of loading. */ }
        }
        return new(installed, enabled, false, enabled
            ? "已安装并启用。首次接入或升级内核后请重启 LOL 客户端，等待状态回传。"
            : "已停用，下次启动生效；如客户端仍有面板，请重启客户端。");
    }

    public PenguPluginStatus Check()
    {
        PenguPluginStatus disk = Inspect();
        string loaderDirectory = string.IsNullOrEmpty(PluginsDirectory) ? "" : Path.GetDirectoryName(PluginsDirectory)!;
        PenguClientRuntime runtime = (runtimeProbe ?? ReadClientRuntime)(loaderDirectory);
        PenguLoaderActivation activation = (activationProbe ?? PenguLoaderActivation.Read)(loaderDirectory);
        string version = "未知";
        if (loaderDirectory.Length > 0)
        {
            string versionFile = Path.Combine(loaderDirectory, "version");
            EnsureRegularPath(versionFile);
            if (File.Exists(versionFile) && new FileInfo(versionFile).Length <= 128) version = File.ReadAllText(versionFile).Trim();
        }
        var lines = new List<string>
        {
            $"插件文件：{(disk.Installed ? "已安装" : "未安装")}",
            $"插件入口：{(disk.Enabled ? "已启用（允许 Loader 加载）" : "未启用")}",
            $"所选 Loader 版本：{version}",
            $"Loader 激活：{activation.Message}",
            $"LOL 客户端：{(runtime.ClientRunning ? "正在运行" : "未检测到运行中的客户端")}",
            "Loader 模块：" + (runtime.LoaderDirectory != null ? "已检测到 · " + runtime.LoaderDirectory
                : runtime.Error != null ? "无法确认 · " + runtime.Error : "未在客户端进程中检测到"),
            $"插件心跳：{(disk.Loaded ? "已收到有效回传" : "未收到有效回传，不能确认插件显示")}",
        };
        if (disk.Loaded) lines.Add(disk.Message);
        lines.Add("");
        lines.Add("下一步：");
        if (!disk.Installed || !disk.Enabled) lines.Add("先安装并启用视觉插件，再完整重启 LOL 客户端。");
        else if (activation.Activated == false && runtime.LoaderDirectory == null)
            lines.Add("请点击本软件的“激活客户端加载”，然后重启 LOL 客户端。");
        else if (!runtime.ClientRunning) lines.Add("点击本软件的“激活客户端加载”，然后启动 LOL 客户端。");
        else if (runtime.Error != null && runtime.LoaderDirectory == null)
            lines.Add("客户端模块读取受限，暂时无法判断 Loader 是否加载。请等待客户端状态回传。");
        else if (runtime.LoaderDirectory == null)
            lines.Add(activation.Activated == true
                ? "Loader 已激活，但当前客户端尚未检测到 Loader 模块。请完整重启 LOL 客户端；若重启后仍未加载，再检查客户端兼容性。"
                : "当前客户端未检测到 Loader 模块。请在本软件激活客户端加载，然后完整退出、重新启动客户端。");
        else if (!Path.TrimEndingDirectorySeparator(runtime.LoaderDirectory).Equals(loaderDirectory, StringComparison.OrdinalIgnoreCase))
            lines.Add("客户端加载的是另一目录的 Loader。请将插件目录改为上述 Loader 下的 plugins，重新安装插件并重启客户端。");
        else if (!disk.Loaded)
            lines.Add("Loader 模块已加载，但这不代表视觉插件已显示。请更新视觉组件并重启客户端；未收到状态回传时，还不能确认视觉组件已运行。");
        else lines.Add("已确认插件回传状态；实际角标还需进入选人页面查看。");
        lines.Add("");
        lines.Add($"插件目录：{(PluginsDirectory.Length > 0 ? PluginsDirectory : "尚未选择")}");
        return disk with { Message = string.Join(Environment.NewLine, lines) };
    }

    private static PenguClientRuntime ReadClientRuntime(string selectedDirectory)
    {
        bool running = false;
        string? detected = null;
        string? error = null;
        foreach (Process process in Process.GetProcessesByName("LeagueClientUx"))
        {
            using (process)
            {
                running = true;
                try
                {
                    foreach (ProcessModule module in process.Modules)
                    {
                        bool direct = module.ModuleName.Equals("core.dll", StringComparison.OrdinalIgnoreCase) || module.ModuleName.Contains("pengu", StringComparison.OrdinalIgnoreCase);
                        if (!direct && !module.ModuleName.Equals("version.dll", StringComparison.OrdinalIgnoreCase)) continue;
                        string fileName = module.FileName;
                        if (!direct)
                        {
                            var target = new FileInfo(fileName).ResolveLinkTarget(returnFinalTarget: true);
                            if (target == null || !target.Name.Equals("core.dll", StringComparison.OrdinalIgnoreCase)) continue;
                            fileName = target.FullName;
                        }
                        string? directory = Path.GetDirectoryName(fileName);
                        if (directory == null) continue;
                        if (directory.Equals(selectedDirectory, StringComparison.OrdinalIgnoreCase)) detected = directory;
                        else if (detected == null && (File.Exists(Path.Combine(directory, "Pengu Loader.exe")) || File.Exists(Path.Combine(directory, "pengu-loader.exe")))) detected = directory;
                    }
                }
                catch (Exception failure) when (failure is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException or IOException or UnauthorizedAccessException)
                { error = "权限不足或客户端进程已退出，请在客户端确认 ✦ 是否显示。"; }
            }
        }
        return new(running, detected, error);
    }

    public void Install()
    {
        string target = Target();
        if (Directory.Exists(target) && ManagedFiles.Any(name => File.Exists(Path.Combine(target, name)))) VerifyOwned(target);
        // Read the complete payload before touching an existing installation.
        string source = bundleProvider?.Invoke() ?? bundleDirectory;
        var files = Payload.ToDictionary(name => name, name => File.ReadAllText(Path.Combine(source, name)));
        bool disabled = File.Exists(Path.Combine(target, "index.js.disabled")) && !File.Exists(Path.Combine(target, "index.js"));
        Directory.CreateDirectory(target);
        foreach (var (name, content) in files)
            AtomicWrite(Path.Combine(target, name == "index.js" && disabled ? "index.js.disabled" : name), content);
    }

    public void SetEnabled(bool enabled)
    {
        string target = Target();
        VerifyOwned(target);
        string source = Path.Combine(target, enabled ? "index.js.disabled" : "index.js");
        string destination = Path.Combine(target, enabled ? "index.js" : "index.js.disabled");
        if (File.Exists(source)) File.Move(source, destination, overwrite: true);
        else if (!File.Exists(destination)) throw new InvalidOperationException("插件文件缺失，请先安装或更新插件。");
    }

    public void Uninstall()
    {
        string target = Target();
        if (!Directory.Exists(target)) return;
        VerifyOwned(target);
        foreach (string name in ManagedFiles) File.Delete(Path.Combine(target, name));
        if (!Directory.EnumerateFileSystemEntries(target).Any()) Directory.Delete(target);
    }

    private string Target()
    {
        if (string.IsNullOrWhiteSpace(PluginsDirectory) || !Directory.Exists(PluginsDirectory))
            throw new InvalidOperationException("插件目录不存在，请重新选择 Pengu Loader 的 plugins 文件夹。");
        EnsureRegularPath(PluginsDirectory);
        string target = Path.Combine(PluginsDirectory, PluginId);
        EnsureRegularPath(target);
        foreach (string file in ManagedFiles) EnsureRegularPath(Path.Combine(target, file));
        return target;
    }

    private static void VerifyOwned(string target)
    {
        string manifest = Path.Combine(target, "pengu.yml");
        string entry = Path.Combine(target, File.Exists(Path.Combine(target, "index.js")) ? "index.js" : "index.js.disabled");
        if (!File.Exists(manifest) || !File.ReadAllLines(manifest).Any(line => line.Trim() == $"id: {PluginId}") ||
            !File.Exists(entry) || !File.ReadAllText(entry).Contains("lol-game-assistant-visual-root", StringComparison.Ordinal))
            throw new InvalidOperationException("同名目录不是可识别的助手插件，未覆盖其中的文件。请另选 Loader 目录或手动检查。");
    }

    private static void EnsureRegularPath(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("插件目录或文件包含符号链接 / 联接，请选择实际目录。");
    }

    private static void AtomicWrite(string path, string content)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, content, new System.Text.UTF8Encoding(false)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string ReadDirectory(string path)
    {
        try { return JsonSerializer.Deserialize<string>(File.ReadAllText(path)) ?? ""; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return ""; }
    }
}
