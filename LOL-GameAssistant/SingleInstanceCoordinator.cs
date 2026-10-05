using System.Diagnostics;
using System.IO.Pipes;

namespace LOL_GameAssistant;

/// <summary>让后启动的实例请求当前实例退出，再接管唯一的管道服务端。</summary>
internal sealed class SingleInstanceCoordinator : IDisposable
{
    private static readonly string PipeName = $"LOL-GameAssistant-{Process.GetCurrentProcess().SessionId}";
    private readonly NamedPipeServerStream _server;
    private readonly CancellationTokenSource _lifetime = new();

    /// <summary>初始化 SingleInstanceCoordinator 的实例状态，并保存传入的依赖或数据。</summary>
    private SingleInstanceCoordinator(NamedPipeServerStream server, Action onReplace)
    {
        _server = server;
        _ = ListenAsync(onReplace);
    }

    /// <summary>取得单实例运行资格，必要时通知已有实例退出。</summary>
    public static SingleInstanceCoordinator? StartOrReplace(Action onReplace)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(15))
        {
            try
            {
                var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                return new SingleInstanceCoordinator(server, onReplace);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            // Another instance still owns the server. Ask it to exit, including
            // when its main window is hidden in the system tray.
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out,
                    PipeOptions.CurrentUserOnly);
                client.Connect(150);
                client.WriteByte(1);
                client.Flush();
            }
            catch (IOException) { }
            catch (TimeoutException) { }
            catch (UnauthorizedAccessException) { }
            Thread.Sleep(100);
        }
        return null;
    }

    /// <summary>等待其他实例发来的替换通知，并响应生命周期取消。</summary>
    private async Task ListenAsync(Action onReplace)
    {
        try
        {
            await _server.WaitForConnectionAsync(_lifetime.Token);
            if (_server.ReadByte() == 1) onReplace();
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    /// <summary>释放当前对象持有的资源，结束相关事件订阅或后台任务。</summary>
    public void Dispose()
    {
        _lifetime.Cancel();
        _server.Dispose();
        _lifetime.Dispose();
    }
}
