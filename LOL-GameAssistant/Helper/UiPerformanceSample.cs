using System.Diagnostics;

namespace LOL_GameAssistant.Helper;

internal sealed class UiPerformanceSample(string operation) : IDisposable
{
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private readonly long _memory = GC.GetTotalMemory(false);
    public void Dispose() => RuntimeDiagnostics.Report("界面性能 · " + operation, "完成",
        $"耗时 {_watch.ElapsedMilliseconds} ms · 托管内存变化 {(GC.GetTotalMemory(false) - _memory) / 1024d:0} KB（非峰值）");
}
