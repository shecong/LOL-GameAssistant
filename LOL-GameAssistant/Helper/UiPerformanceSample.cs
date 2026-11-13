using System.Diagnostics;

namespace LOL_GameAssistant.Helper;

internal sealed class UiPerformanceSample : IDisposable
{
    private readonly string _operation;
    private readonly Func<string>? _detail;
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private readonly long _memory = GC.GetTotalMemory(false);

    internal UiPerformanceSample(string operation, Func<string>? detail = null)
    {
        _operation = operation;
        _detail = detail;
    }

    public void Dispose()
    {
        string suffix = _detail?.Invoke() is { Length: > 0 } text ? text + " · " : "";
        RuntimeDiagnostics.Report("界面性能 · " + _operation, "完成",
            $"{suffix}耗时 {_watch.ElapsedMilliseconds} ms · 托管内存变化 {(GC.GetTotalMemory(false) - _memory) / 1024d:0} KB（非峰值）");
    }
}
