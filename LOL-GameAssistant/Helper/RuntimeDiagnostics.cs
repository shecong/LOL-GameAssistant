using System.Collections.Concurrent;
using System.Text.RegularExpressions;

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
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>将异常信息写入运行日志。</summary>
    public static void WriteException(Exception exception) =>
        AppendLog($"未处理异常 | {exception.GetType().Name} | {exception.Message}\n{exception.StackTrace}");

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