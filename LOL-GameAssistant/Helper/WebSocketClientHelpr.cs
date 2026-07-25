using System.Buffers;
using System.Net.WebSockets;
using System.Text;

namespace LOL_GameAssistant.Helper
{
    /// <summary>
    /// WebSocket客户端，支持认证和心跳
    /// </summary>
    public class WebSocketClient : IDisposable, IAsyncDisposable
    {
        private ClientWebSocket? _socket;
        private CancellationTokenSource _cts;
        private readonly string _url;
        private readonly string _token;
        private volatile bool _isRunning;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly object _cleanupLock = new object();
        private CancellationTokenSource? _heartbeatCts;
        private Task? _receiveTask;
        private Task? _heartbeatTask;

        /// <summary>
        /// 当前连接状态
        /// </summary>
        public bool IsConnected => _isRunning && _socket?.State == WebSocketState.Open;

        /// <summary>
        /// 收到消息时触发
        /// </summary>
        public event Action<string>? OnMessage;

        /// <summary>
        /// 发生错误时触发
        /// </summary>
        public event Action<Exception>? OnError;

        /// <summary>
        /// 连接状态变化时触发
        /// </summary>
        public event Action<bool>? OnConnectChanged;

        public WebSocketClient(string url, string? token = null)
        {
            _url = url ?? throw new ArgumentNullException(nameof(url));
            _token = token ?? string.Empty;
            _cts = new CancellationTokenSource();
        }

        /// <summary>
        /// 安全触发事件（捕获异常防止传播）
        /// </summary>
        private static void SafeInvoke<T>(Action<T>? handler, T arg)
        {
            try { handler?.Invoke(arg); } catch { }
        }

        /// <summary>
        /// 连接到WebSocket服务器
        /// </summary>
        public async Task ConnectAsync()
        {
            if (_isRunning)
                throw new InvalidOperationException("客户端已经在运行");

            await CleanupAsync().ConfigureAwait(true);

            _socket = new ClientWebSocket();

            if (!string.IsNullOrEmpty(_token))
            {
                _socket.Options.SetRequestHeader("Authorization", $"Basic {_token}");
            }

            // 跳过SSL验证（仅用于本地LCU连接）
            _socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;

            try
            {
                await _socket.ConnectAsync(new Uri(_url), _cts.Token).ConfigureAwait(false);
                _isRunning = true;

                SafeInvoke(OnConnectChanged, true);

                // 启动接收循环
                _receiveTask = Task.Run(() => ReceiveLoopAsync(), _cts.Token);

                // 启动心跳任务
                _heartbeatCts = new CancellationTokenSource();
                _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(), _heartbeatCts.Token);
            }
            catch (Exception ex)
            {
                _isRunning = false;
                SafeInvoke(OnError, new Exception($"连接失败: {ex.Message}", ex));
            }
        }

        /// <summary>
        /// 清理资源（带重入保护）
        /// </summary>
        private async Task CleanupAsync()
        {
            lock (_cleanupLock)
            {
                if (!_isRunning && _socket == null) return;
                _isRunning = false;
            }

            // 停止心跳
            if (_heartbeatCts != null)
            {
                try { _heartbeatCts.Cancel(); } catch { }
                _heartbeatCts.Dispose();
                _heartbeatCts = null;
            }

            // 关闭并释放 WebSocket
            var socket = _socket;
            _socket = null;
            if (socket != null)
            {
                try
                {
                    if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                    {
                        using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "清理连接", closeCts.Token).ConfigureAwait(false);
                    }
                }
                catch { }

                try { socket.Dispose(); } catch { }
            }

            // 等待任务完成（带超时）
            var tasks = new List<Task>();
            if (_receiveTask is { IsCompleted: false }) tasks.Add(_receiveTask);
            if (_heartbeatTask is { IsCompleted: false }) tasks.Add(_heartbeatTask);

            if (tasks.Count > 0)
            {
                try { await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(1000)).ConfigureAwait(false); } catch { }
            }
        }

        /// <summary>
        /// 发送消息到服务器（线程安全）
        /// </summary>
        public async Task SendAsync(string message)
        {
            if (!IsConnected) return;

            await _sendLock.WaitAsync(_cts.Token).ConfigureAwait(false);
            try
            {
                var socket = _socket; // 本地快照
                if (socket == null || socket.State != WebSocketState.Open) return;

                var buffer = Encoding.UTF8.GetBytes(message);
                await socket.SendAsync(
                    new ArraySegment<byte>(buffer),
                    WebSocketMessageType.Text,
                    true,
                    _cts.Token
                ).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        /// <summary>
        /// 接收消息循环
        /// </summary>
        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[4096];
            var segments = new List<ArraySegment<byte>>();

            while (_isRunning && !_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var socket = _socket;
                    if (socket == null || socket.State != WebSocketState.Open)
                    {
                        await Task.Delay(100, _cts.Token).ConfigureAwait(false);
                        continue;
                    }

                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await CloseAsync().ConfigureAwait(false);
                        break;
                    }

                    segments.Add(new ArraySegment<byte>(buffer, 0, result.Count));

                    if (!result.EndOfMessage)
                        continue;

                    // 拼接完整消息
                    var totalBytes = segments.Sum(s => s.Count);
                    var messageBytes = new byte[totalBytes];
                    var offset = 0;

                    foreach (var segment in segments)
                    {
                        Buffer.BlockCopy(segment.Array!, segment.Offset, messageBytes, offset, segment.Count);
                        offset += segment.Count;
                    }

                    segments.Clear();
                    var message = Encoding.UTF8.GetString(messageBytes);

                    SafeInvoke(OnMessage, message);

                    // 处理心跳消息
                    if (message == "PING")
                        await SendAsync("PONG").ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (WebSocketException wsEx)
                {
                    if (_isRunning)
                    {
                        SafeInvoke(OnError, wsEx);
                        await CloseAsync().ConfigureAwait(false);
                    }
                    break;
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                    {
                        SafeInvoke(OnError, ex);
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// 心跳循环
        /// </summary>
        private async Task HeartbeatLoopAsync()
        {
            while (_isRunning && _heartbeatCts?.IsCancellationRequested != true)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), _heartbeatCts?.Token ?? CancellationToken.None).ConfigureAwait(false);

                    if (IsConnected)
                    {
                        await SendAsync("PING").ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // 心跳异常静默忽略
                }
            }
        }

        /// <summary>
        /// 关闭WebSocket连接
        /// </summary>
        public async Task CloseAsync()
        {
            var wasRunning = _isRunning;
            await CleanupAsync().ConfigureAwait(false);

            if (wasRunning)
            {
                SafeInvoke(OnConnectChanged, false);
            }
        }

        /// <summary>
        /// 释放资源（仅异步版本，避免同步阻塞）
        /// </summary>
        public void Dispose()
        {
            // 不阻塞 UI 线程——fire-and-forget 异步清理
            _ = DisposeAsync();
        }

        /// <summary>
        /// 异步释放资源
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (_isRunning)
            {
                _cts?.Cancel();
                _isRunning = false;
            }

            await CleanupAsync().ConfigureAwait(false);

            _cts?.Dispose();
            _heartbeatCts?.Dispose();
            _sendLock?.Dispose();

            // 清除事件引用
            OnMessage = null;
            OnError = null;
            OnConnectChanged = null;

            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// WebSocket客户端（简化版本）
    /// </summary>
    public class SimpleWebSocketClient : WebSocketClient, IDisposable, IAsyncDisposable
    {
        public SimpleWebSocketClient(string url, string? token = null)
            : base(url, token)
        {
        }

        public new async ValueTask DisposeAsync()
        {
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
