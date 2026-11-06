using System.Diagnostics;
using System.Text;

namespace LOL_GameAssistant.Infrastructure.Skins;

public sealed class CslolToolRunner(string executable) : ICslolToolRunner
{
    private Process Create(IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return new() { StartInfo = start };
    }

    public async Task<CslolToolResult> RunAsync(IReadOnlyList<string> arguments, Action<string> status, CancellationToken token)
    {
        using var process = Create(arguments);
        if (!process.Start()) return new(false, "无法启动 mod-tools");
        var output = ReadAsync(process.StandardOutput, status);
        var error = ReadAsync(process.StandardError, status);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(10));
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            Kill(process); await process.WaitForExitAsync().ConfigureAwait(false);
            await Task.WhenAll(output, error).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); return new(false, "mod-tools 运行超时");
        }
        var messages = await Task.WhenAll(output, error).ConfigureAwait(false);
        return new(process.ExitCode == 0, string.Join('\n', messages));
    }

    public async Task<ICslolOverlaySession> StartOverlayAsync(IReadOnlyList<string> arguments, Action<string> status, CancellationToken token)
    {
        var process = Create(arguments);
        // runoverlay uses stdin as its lifetime channel. Inheriting a GUI/closed
        // input handle makes it exit immediately after printing "Waiting".
        process.StartInfo.RedirectStandardInput = true;
        try
        {
            if (!process.Start()) throw new IOException("无法启动文件换肤进程");
            var session = new OverlaySession(process, status);
            try
            {
                await Task.Delay(750, token).ConfigureAwait(false);
                if (!session.IsRunning) throw new IOException("文件换肤进程启动后退出：" + await session.Output.ConfigureAwait(false));
                return session;
            }
            catch { session.Dispose(); throw; }
        }
        catch { process.Dispose(); throw; }
    }

    private static async Task<string> ReadAsync(StreamReader stream, Action<string> status)
    {
        var buffer = new StringBuilder();
        while (await stream.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            buffer.AppendLine(line); if (buffer.Length > 8192) buffer.Remove(0, buffer.Length - 8192);
            if (line.Contains("indexing", StringComparison.OrdinalIgnoreCase)) status("正在读取游戏索引…");
            else if (line.Contains("waiting", StringComparison.OrdinalIgnoreCase)) status("文件换肤正在等待游戏状态…");
        }
        return buffer.ToString();
    }
    private static void Kill(Process process) { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    private sealed class OverlaySession : ICslolOverlaySession
    {
        private readonly Process _process;
        private int _disposed;
        public OverlaySession(Process process, Action<string> status)
        {
            _process = process; Pid = process.Id;
            Output = MonitorAsync(status);
        }
        public Task<string> Output { get; }
        private async Task<string> MonitorAsync(Action<string> status)
        {
            var reads = Task.WhenAll(ReadAsync(_process.StandardOutput, status), ReadAsync(_process.StandardError, status));
            var lines = await reads.ConfigureAwait(false);
            await _process.WaitForExitAsync().ConfigureAwait(false);
            status(Volatile.Read(ref _disposed) == 0 ? "文件换肤进程已退出：" + string.Join('\n', lines) : "文件换肤进程已停止");
            return string.Join('\n', lines);
        }
        public bool IsRunning => Volatile.Read(ref _disposed) == 0 && !_process.HasExited;
        public int Pid { get; }
        public void Dispose()
        {
            lock (_process)
            {
                if (_disposed != 0) return;
                Kill(_process);
                if (!_process.WaitForExit(5000)) throw new IOException("文件换肤进程尚未退出，不能切换引擎");
                Volatile.Write(ref _disposed, 1);
                _ = Output.ContinueWith(_ => _process.Dispose(), TaskScheduler.Default);
            }
        }
    }
}
