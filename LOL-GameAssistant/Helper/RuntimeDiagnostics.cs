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

    public static void WriteException(Exception exception) =>
        AppendLog($"未处理异常 | {exception.GetType().Name} | {exception.Message}\n{exception.StackTrace}");

    public static string GetLogPath() => LogPath;

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

    public static IReadOnlyList<DiagnosticEntry> Snapshot() =>
        Entries.Values.OrderBy(entry => entry.Component, StringComparer.OrdinalIgnoreCase).ToArray();
}

public sealed record DiagnosticEntry(string Component, string Status, string Detail, DateTimeOffset UpdatedAt);