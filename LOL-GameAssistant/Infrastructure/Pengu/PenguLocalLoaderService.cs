using System.Text.Json;
using LOL_GameAssistant.Application.ClientFeatures;

namespace LOL_GameAssistant.Infrastructure.Pengu;

/// <summary>使用选定的本地组件，并支持从 Gitee 安装已校验的发行包。</summary>
public sealed class PenguLocalLoaderService(string settingsFile, string? defaultDirectory = null) : IPenguLocalLoaderService
{
    public string SourceDirectory { get; private set; } = ReadDirectory(settingsFile, defaultDirectory ?? PenguReleaseInstaller.DefaultDirectory);

    public void SelectDirectory(string directory)
    {
        string path = Path.GetFullPath(directory.Trim().Trim('"'));
        Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(settingsFile))!);
        File.WriteAllText(settingsFile, JsonSerializer.Serialize(path));
        SourceDirectory = path;
    }

    public PreparedPenguLoader Prepare() => Resolve(SourceDirectory);

    public async Task<PreparedPenguLoader> InstallFromGiteeAsync(CancellationToken cancellationToken = default)
    {
        string? previousPlugins = null;
        string? previousConfig = null;
        try
        {
            var previous = Prepare();
            previousPlugins = previous.PluginsDirectory;
            string config = Path.Combine(Path.GetDirectoryName(previous.CorePath)!, "config");
            if (File.Exists(config)) previousConfig = File.ReadAllText(config);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or JsonException) { }
        string directory = await new PenguReleaseInstaller().InstallAsync(cancellationToken).ConfigureAwait(false);
        if (previousPlugins != null && !Path.GetFullPath(SourceDirectory).Equals(directory, StringComparison.OrdinalIgnoreCase))
            PreserveConfiguration(Path.Combine(directory, "bin", "config"), previousConfig ?? "", previousPlugins);
        SelectDirectory(directory);
        return Prepare();
    }

    internal static void PreserveConfiguration(string file, string config, string plugins)
    {
        var lines = config.Replace("\r\n", "\n").Split('\n').ToList();
        int app = lines.FindIndex(line => line.Trim().Equals("[app]", StringComparison.OrdinalIgnoreCase));
        if (app < 0) { lines.Add("[app]"); app = lines.Count - 1; }
        int end = lines.FindIndex(app + 1, line => line.Trim().StartsWith('['));
        if (end < 0) end = lines.Count;
        int key = lines.FindIndex(app + 1, end - app - 1, line => line.Split('=', 2)[0].Trim() == "plugins_dir");
        string value = "plugins_dir = " + plugins;
        if (key >= 0) lines[key] = value; else lines.Insert(end, value);
        File.WriteAllText(file, string.Join(Environment.NewLine, lines));
    }

    public void SetActivated(bool enabled)
    {
        string core = Prepare().CorePath;
        using var machine = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
        using var key = machine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\LeagueClientUx.exe", true);
        string? current = key.GetValue("Debugger") as string;
        if (!string.IsNullOrWhiteSpace(current) && !PenguLoaderActivation.MatchesDebugger(current, core))
            throw new InvalidOperationException("客户端加载入口由其他程序占用，请先停用原来的加载器。");
        if (enabled) key.SetValue("Debugger", $"rundll32 \"{core}\", #6000");
        else key.DeleteValue("Debugger", false);
    }

    private static PreparedPenguLoader Resolve(string source)
    {
        string bin = Path.Combine(source, "bin");
        string metadata = Path.Combine(source, "loader", "src-tauri", "tauri.conf.json");
        if (!File.Exists(metadata)) throw new InvalidOperationException("请先从 Gitee 安装视觉组件，或选择本地 PenguLoader 组件 / 源码根目录。");
        string core = Path.Combine(bin, "core.dll");
        if (!File.Exists(core))
            throw new InvalidOperationException("本地 Loader 尚未构建完成，请运行源码目录中的 scripts/prepare-game-assistant.ps1 -BuildCore，生成 bin/core.dll。");
        using var json = JsonDocument.Parse(File.ReadAllText(metadata));
        string version = json.RootElement.GetProperty("package").GetProperty("version").GetString() ?? "本地构建";
        string plugins = Path.Combine(bin, "plugins");
        string config = Path.Combine(bin, "config");
        if (File.Exists(config))
        {
            bool app = false;
            foreach (string line in File.ReadLines(config))
            {
                string text = line.Trim();
                if (text.StartsWith('[')) { app = text.Equals("[app]", StringComparison.OrdinalIgnoreCase); continue; }
                string[] parts = text.Split('=', 2);
                if (app && parts.Length == 2 && parts[0].Trim() == "plugins_dir" && parts[1].Trim().Length > 0)
                {
                    string custom = parts[1].Trim().Trim('"');
                    if (!Path.IsPathFullyQualified(custom)) throw new InvalidOperationException("本地 Loader 的 plugins_dir 必须使用绝对路径。");
                    plugins = Path.GetFullPath(custom);
                }
            }
        }
        Directory.CreateDirectory(plugins);
        return new(version, core, plugins);
    }

    private static string ReadDirectory(string file, string fallback)
    {
        try { return JsonSerializer.Deserialize<string>(File.ReadAllText(file)) is { Length: > 0 } value ? value : fallback; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return fallback; }
    }
}
