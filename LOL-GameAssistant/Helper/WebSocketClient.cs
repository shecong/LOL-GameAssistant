using System.Net.WebSockets;
using System.Text;

namespace LOL_GameAssistant.Helper;

public sealed class WebSocketClientOptions
{
    public int MaxMessageBytes { get; init; } = 1024 * 1024;
    public TimeSpan FragmentTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>每条连接只有一个接收/事件消费者；等待业务处理提供背压，不创建消息任务队列。</summary>
public class WebSocketClient : IDisposable, IAsyncDisposable
{
    private readonly Uri _uri;
    private readonly string? _token;
    private readonly WebSocketClientOptions _options;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _lifetime;
    private Task? _runTask;
    private volatile bool _isRunning;
    private volatile bool _reconnectEnabled = true;
    private int _reconnectDelayMs = 5000;
    private int _maxReconnectDelayMs = 30000;
    private int _disposed;

    public bool IsConnected => _isRunning && _socket?.State == WebSocketState.Open;
    public event Action<string>? OnMessage;
    public event Func<string, CancellationToken, Task>? OnMessageAsync;
    public event Action<Exception>? OnError;
    public event Action<bool>? OnConnectChanged;
    public event Action<string>? OnReconnecting;
    public bool ReconnectEnabled { get => _reconnectEnabled; set => _reconnectEnabled = value; }
    public int ReconnectDelayMs { get => _reconnectDelayMs; set => _reconnectDelayMs = Math.Max(1000, value); }
    public int MaxReconnectDelayMs { get => _maxReconnectDelayMs; set => _maxReconnectDelayMs = Math.Max(1000, value); }

    public WebSocketClient(string url, string? token = null, WebSocketClientOptions? options = null)
    {
        _uri = new Uri(url ?? throw new ArgumentNullException(nameof(url)));
        if (_uri.Scheme is not ("ws" or "wss")) throw new ArgumentException("Expected a WebSocket URL.", nameof(url));
        _token = token;
        _options = options ?? new();
        if (_options.MaxMessageBytes is < 1 or > 16 * 1024 * 1024 ||
            _options.FragmentTimeout <= TimeSpan.Zero || _options.FragmentTimeout > TimeSpan.FromMinutes(5) ||
            _options.ConnectTimeout <= TimeSpan.Zero || _options.ConnectTimeout > TimeSpan.FromMinutes(5))
            throw new ArgumentException("Invalid WebSocket limits.", nameof(options));
    }

    public void StopReconnect() => _reconnectEnabled = false;

    public async Task ConnectAsync()
    {
        Task<bool> firstAttempt;
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_runTask is { IsCompleted: false }) throw new InvalidOperationException("客户端已经在运行");
            _lifetime?.Dispose();
            _lifetime = new CancellationTokenSource();
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            firstAttempt = started.Task;
            var cancellationToken = _lifetime.Token;
            _runTask = Task.Run(() => RunConnectionLoopAsync(started, cancellationToken));
        }
        finally { _lifecycle.Release(); }
        await firstAttempt.ConfigureAwait(false);
    }

    private ClientWebSocket CreateSocket()
    {
        var socket = new ClientWebSocket();
        if (!string.IsNullOrEmpty(_token)) socket.Options.SetRequestHeader("Authorization", $"Basic {_token}");
        // 本机 LCU 使用自签名证书；远程目标仍执行正常证书校验。
        if (_uri.Host == "127.0.0.1")
            socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        return socket;
    }

    private async Task RunConnectionLoopAsync(TaskCompletionSource<bool> started, CancellationToken lifetime)
    {
        int attempt = 0;
        int delay = Math.Min(_reconnectDelayMs, _maxReconnectDelayMs);
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                bool connected = false;
                using var session = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
                using var socket = CreateSocket();
                _socket = socket;
                Task? heartbeat = null;
                try
                {
                    using var connect = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
                    connect.CancelAfter(_options.ConnectTimeout);
                    await socket.ConnectAsync(_uri, connect.Token).ConfigureAwait(false);
                    connected = _isRunning = true;
                    if (attempt > 0)
                        await SendOnSocketAsync(socket, "[5, \"OnJsonApiEvent\"]", session.Token).ConfigureAwait(false);
                    Notify(OnConnectChanged, true);
                    started.TrySetResult(true);
                    if (attempt > 0) Notify(OnReconnecting, "WebSocket 重连成功");
                    delay = Math.Min(_reconnectDelayMs, _maxReconnectDelayMs);
                    heartbeat = HeartbeatAsync(socket, session);
                    await WebSocketMessagePump.RunAsync(socket, DispatchMessageAsync,
                        (message, token) => SendOnSocketAsync(socket, message, token), _options, session.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                catch (Exception ex) { ReportError(new Exception($"WebSocket 连接或接收失败: {ex.Message}", ex)); }
                finally
                {
                    _isRunning = false;
                    session.Cancel();
                    socket.Abort();
                    if (heartbeat != null)
                    {
                        try { await heartbeat.ConfigureAwait(false); }
                        catch (OperationCanceledException) { }
                    }
                    Interlocked.CompareExchange(ref _socket, null, socket);
                    if (connected) Notify(OnConnectChanged, false);
                    started.TrySetResult(false);
                }
                if (lifetime.IsCancellationRequested || !_reconnectEnabled) break;
                attempt++;
                Notify(OnReconnecting, $"正在重连... 第 {attempt} 次尝试，等待 {delay / 1000} 秒");
                await Task.Delay(delay, lifetime).ConfigureAwait(false);
                delay = (int)Math.Min((long)delay * 2, _maxReconnectDelayMs);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { started.TrySetResult(false); }
    }

    private async Task DispatchMessageAsync(string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (Action<string> handler in OnMessage?.GetInvocationList() ?? [])
        {
            try { handler(message); }
            catch (Exception ex) { ReportError(ex); }
        }
        foreach (Func<string, CancellationToken, Task> handler in OnMessageAsync?.GetInvocationList() ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { await handler(message, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) { ReportError(ex); }
        }
    }

    private async Task HeartbeatAsync(ClientWebSocket socket, CancellationTokenSource session)
    {
        try
        {
            while (!session.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), session.Token).ConfigureAwait(false);
                await SendOnSocketAsync(socket, "[8,\"PING\"]", session.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (session.IsCancellationRequested) { }
        catch (Exception ex)
        {
            ReportError(ex);
            session.Cancel();
            socket.Abort();
        }
    }

    public async Task SendAsync(string message)
    {
        var socket = _socket;
        var lifetime = _lifetime;
        if (socket == null || lifetime == null || !IsConnected) return;
        await SendOnSocketAsync(socket, message, lifetime.Token).ConfigureAwait(false);
    }

    private async Task SendOnSocketAsync(ClientWebSocket socket, string message, CancellationToken cancellationToken)
    {
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (socket.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(message);
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken)
                .ConfigureAwait(false);
        }
        finally { _sendLock.Release(); }
    }

    private void Notify<T>(Action<T>? handlers, T value)
    {
        foreach (Action<T> handler in handlers?.GetInvocationList() ?? [])
        {
            try { handler(value); }
            catch (Exception ex) { ReportError(ex); }
        }
    }

    private void ReportError(Exception error)
    {
        foreach (Action<Exception> handler in OnError?.GetInvocationList() ?? [])
        {
            try { handler(error); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.Message); }
        }
    }

    public async Task CloseAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            StopReconnect();
            _lifetime?.Cancel();
            _socket?.Abort();
            if (_runTask != null) await _runTask.ConfigureAwait(false);
            _runTask = null;
            _lifetime?.Dispose();
            _lifetime = null;
        }
        finally { _lifecycle.Release(); }
    }

    public void Dispose() => _ = FinishDisposeAsync();

    private async Task FinishDisposeAsync()
    {
        try { await DisposeAsync().ConfigureAwait(false); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.Message); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await CloseAsync().ConfigureAwait(false);
        _lifetime?.Dispose();
        GC.SuppressFinalize(this);
    }
}

public class SimpleWebSocketClient : WebSocketClient
{
    public SimpleWebSocketClient(string url, string? token = null) : base(url, token) { }
}
