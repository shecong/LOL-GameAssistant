using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using LOL_GameAssistant.Domain.Skins;

namespace LOL_GameAssistant.Helper;

/// <summary>
/// Lightweight, in-process diagnostics for local integrations.  It deliberately
/// never stores an LCU token, chat text, or other secret values.
/// </summary>
public static class RuntimeDiagnostics
{
    private static readonly ConcurrentDictionary<string, DiagnosticEntry> Entries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object LogSync = new();

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LOL-GameAssistant", "diagnostics.log");

    public static event EventHandler? Changed;
    public static event Action<DiagnosticEntry>? Reported;
    public const string SkinCoreComponent = "换肤核心";

    public static void ReportSkinCoreFailure(SkinCoreException error)
    {
        string reason = error.Code switch
        {
            "message_post_failed" when error.Details?.Win32 == 5 => "发送消息被拒绝访问，请检查助手与游戏的权限级别及网吧管控策略。",
            "message_post_failed" => "向游戏发送消息失败。",
            "core_response_timeout" => "游戏端响应超时，请待对局加载稳定后读取。",
            "host_response_timeout" => "核心宿主响应超时。",
            "request_expired" => "核心请求已过期。",
            "host_start_failed" => "核心宿主启动失败。",
            "load_own_bridge_failed" => "核心 DLL 加载失败，请检查完整运行包。",
            "standard_windows_hook_rejected" => "Windows 拒绝核心连接。",
            "core_files_missing" or "hybrid_files_missing" => "核心运行文件缺失，请检查完整运行包。",
            _ => "核心请求未完成。"
        };
        if (error.OutcomeUnknown) reason = "请求结果未确认，动作可能已执行。请读取状态，勿自动重试。";
        Report(SkinCoreComponent, error.OutcomeUnknown ? "结果未确认" : "请求失败",
            $"{reason}\n错误码：{error.Code}\n诊断详情：{JsonSerializer.Serialize(error.Details)}");
    }
    public static void ReportStartup()
    {
        var assembly = System.Reflection.Assembly.GetEntryAssembly() ?? typeof(RuntimeDiagnostics).Assembly;
        string version = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString() ?? "未知";
        string path = Environment.ProcessPath ?? AppContext.BaseDirectory;
        string buildFile = string.IsNullOrWhiteSpace(assembly.Location) ? path : assembly.Location;
        Report("启动版本", version, $"构建文件时间 {File.GetLastWriteTime(buildFile):yyyy-MM-dd HH:mm:ss} | 运行路径 {path}");
    }

    /// <summary>记录一条运行诊断状态并通知订阅者。</summary>
    public static void Report(string component, string status, string detail)
    {
        var entry = new DiagnosticEntry(component, status, detail, DateTimeOffset.Now);
        bool changed = !Entries.TryGetValue(component, out DiagnosticEntry? previous) ||
            previous.Status != status || previous.Detail != detail;
        Entries[component] = entry;
        if (changed)
        {
            AppendLog($"{component} | {status} | {detail}");
            Reported?.Invoke(entry);
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>将异常信息写入运行日志。</summary>
    public static void WriteException(Exception exception)
    {
        string native = "";
        if (exception is Win32Exception error)
        {
            using var process = Process.GetCurrentProcess();
            native = $" | Win32={error.NativeErrorCode} | USER={GetGuiResources(process.Handle, 1)}" +
                $" | GDI={GetGuiResources(process.Handle, 0)}";
        }
        AppendLog($"异常 | {exception.GetType().Name} | {exception.Message}{native}\n{exception.StackTrace}");
    }

    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr process, uint flags);

    /// <summary>取得运行日志文件的位置。</summary>
    public static string GetLogPath() => LogPath;

    /// <summary>将诊断文本追加到日志文件。</summary>
    private static void AppendLog(string value)
    {
        try
        {
            string safe = Regex.Replace(value,
                @"(?i)(bearer\s+|basic\s+|api[_-]?key\s*[:=]\s*|token\s*[:=]\s*)[^\s&]+",
                "$1[REDACTED]");
            if (safe.Length > 4000) safe = safe[..4000] + "…";
            lock (LogSync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1024 * 1024)
                    File.Move(LogPath, LogPath + ".old", overwrite: true);
                File.AppendAllText(LogPath, $"[{DateTimeOffset.Now:O}] {safe}{Environment.NewLine}");
            }
        }
        catch { /* 诊断日志不能影响主流程。 */ }
    }

    /// <summary>返回当前诊断记录的快照，供界面展示。</summary>
    public static IReadOnlyList<DiagnosticEntry> Snapshot() =>
        Entries.Values.OrderBy(entry => entry.Component, StringComparer.OrdinalIgnoreCase).ToArray();
}

/// <summary>运行诊断中的一条状态记录。</summary>
public sealed record DiagnosticEntry(string Component, string Status, string Detail, DateTimeOffset UpdatedAt);
