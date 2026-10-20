using System.Diagnostics;

namespace LOL_GameAssistant.Helper;

/// <summary>Paired markers leave the last unfinished startup operation visible in diagnostics.log.</summary>
internal sealed class StartupDiagnostics : IDisposable
{
    private readonly string _component;
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private bool _disposed;
    internal StartupDiagnostics(string stage)
    {
        _component = "启动 · " + stage;
        RuntimeDiagnostics.Report(_component, "开始", "正在初始化");
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RuntimeDiagnostics.Report(_component, "结束", $"耗时 {_elapsed.ElapsedMilliseconds} ms");
    }
}
