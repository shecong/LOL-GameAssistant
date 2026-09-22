using System.Collections.Concurrent;

namespace LOL_GameAssistant.Helper;

/// <summary>
/// Lightweight, in-process diagnostics for local integrations.  It deliberately
/// never stores an LCU token, chat text, or other secret values.
/// </summary>
public static class RuntimeDiagnostics
{
    private static readonly ConcurrentDictionary<string, DiagnosticEntry> Entries = new(StringComparer.OrdinalIgnoreCase);

    public static event EventHandler? Changed;

    public static void Report(string component, string status, string detail)
    {
        Entries[component] = new DiagnosticEntry(component, status, detail, DateTimeOffset.Now);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static IReadOnlyList<DiagnosticEntry> Snapshot() =>
        Entries.Values.OrderBy(entry => entry.Component, StringComparer.OrdinalIgnoreCase).ToArray();
}

public sealed record DiagnosticEntry(string Component, string Status, string Detail, DateTimeOffset UpdatedAt);