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
        private ClientWebSocket _socket;
        private CancellationTokenSource _cts;
        private readonly string _url;
        private readonly string _token;
        private volatile bool _isRunning;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _heartbeatCts;
        private Task _receiveTask;
        private Task _heartbeatTask;

        /// <summary>
        /// 当前连接状态
        /// </summary>
        public bool IsConnected => _socket?.State == WebSocketState.Open && _isRunning;

        /// <summary>
        /// 收到消息时触发
        /// </summary>
        public event Action<string> OnMessage;

        /// <summary>
        /// 发生错误时触发
        /// </summary>
        public event Action<Exception> OnError;

        /// <summary>
        /// 连接状态变化时触发
        /// </summary>
        public event Action<bool> OnConnectChanged;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="url">WebSocket服务器地址</param>
        /// <param name="token">认证令牌（可选）</param>
        public WebSocketClient(string url, string token = null)
        {
            _url = url ?? throw new ArgumentNullException(nameof(url));
            _token = token;
            _cts = new CancellationTokenSource();
        }

        /// <summary>
        /// 连接到WebSocket服务器
        /// </summary>
        /// <exception cref="InvalidOperationException">已经连接</exception>
        /// <exception cref="Exception">连接失败</exception>
        public async Task ConnectAsync()
        {
            if (_isRunning)
                throw new InvalidOperationException("客户端已经在运行");

            // 清理之前的连接
            await CleanupAsync().ConfigureAwait(true);

            _socket = new ClientWebSocket();

            // 添加认证头（Basic认证）
            if (!string.IsNullOrEmpty(_token))
            {
                _socket.Options.SetRequestHeader("Authorization", $"Basic {_token}");
            }

            // 跳过SSL验证（仅用于本地测试）
            _socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;

            try
            {
                // 连接到服务器
                await _socket.ConnectAsync(new Uri(_url), _cts.Token).ConfigureAwait(false);
                _isRunning = true;

                // 触发连接状态变化事件（异步触发，不等待）
                _ = Task.Run(() => OnConnectChanged?.Invoke(true));

                // 启动接收循环
                _receiveTask = Task.Run(() => ReceiveLoopAsync(), _cts.Token);

                // 启动心跳任务
                _heartbeatCts = new CancellationTokenSource();
                _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(), _heartbeatCts.Token);
            }
            catch (Exception ex)
            {
                _isRunning = false;
                // 异步触发错误事件
                _ = Task.Run(() => OnError?.Invoke(new Exception($"连接失败: {ex.Message}", ex)));
            }
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        private async Task CleanupAsync()
        {
            _isRunning = false;

            // 停止心跳任务
            if (_heartbeatCts != null)
            {
                await _heartbeatCts.CancelAsync().ConfigureAwait(false);
                _heartbeatCts.Dispose();
                _heartbeatCts = null;
            }

            // 关闭WebSocket
            if (_socket != null && _socket.State == WebSocketState.Open)
            {
                try
                {
                    await _socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "清理连接",
                        CancellationToken.None
                    ).ConfigureAwait(false);
                }
                catch { }

                _socket.Dispose();
                _socket = null;
            }

            // 等待任务完成，但设置超时
            var tasks = new List<Task>();
            if (_receiveTask != null && !_receiveTask.IsCompleted)
                tasks.Add(_receiveTask);
            if (_heartbeatTask != null && !_heartbeatTask.IsCompleted)
                tasks.Add(_heartbeatTask);

            if (tasks.Count > 0)
            {
                await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(1000)).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 发送消息到服务器
        /// </summary>
        /// <param name="message">要发送的消息</param>
        /// <exception cref="InvalidOperationException">未连接</exception>
        public async Task SendAsync(string message)
        {
            if (!IsConnected) return;
            //throw new InvalidOperationException("未连接到服务器");

            await _sendLock.WaitAsync(_cts.Token).ConfigureAwait(false);
            try
            {
                var buffer = Encoding.UTF8.GetBytes(message);
                await _socket.SendAsync(
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
        /// 接收消息的循环
        /// </summary>
        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[4096];
            var segments = new List<ArraySegment<byte>>();

            while (_isRunning && !_cts.Token.IsCancellationRequested)
            {
                try
                {
                    if (_socket == null || _socket.State != WebSocketState.Open)
                    {
                        await Task.Delay(100, _cts.Token).ConfigureAwait(false);
                        continue;
                    }

                    var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _ = Task.Run(() => CloseAsync());
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
                        Buffer.BlockCopy(segment.Array, segment.Offset, messageBytes, offset, segment.Count);
                        offset += segment.Count;
                    }

                    segments.Clear();
                    var message = Encoding.UTF8.GetString(messageBytes);

                    // 异步触发消息事件
                    _ = Task.Run(() => OnMessage?.Invoke(message));

                    // 处理心跳消息
                    if (message == "PING")
                        _ = Task.Run(() => SendAsync("PONG"));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (WebSocketException wsEx)
                {
                    if (_isRunning)
                    {
                        // 异步触发错误事件
                        _ = Task.Run(() => OnError?.Invoke(wsEx));
                        _ = Task.Run(() => CloseAsync());
                    }
                    break;
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                    {
                        // 异步触发错误事件
                        _ = Task.Run(() => OnError?.Invoke(ex));
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// 心跳循环，定期发送心跳消息
        /// </summary>
        private async Task HeartbeatLoopAsync()
        {
            while (_isRunning && _heartbeatCts?.Token.IsCancellationRequested != true)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), _heartbeatCts.Token).ConfigureAwait(false);

                    if (IsConnected)
                    {
                        _ = Task.Run(() => SendAsync("PING"));
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // 异步触发错误事件
                    _ = Task.Run(() => OnError?.Invoke(ex));
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
                // 异步触发连接状态变化事件
                _ = Task.Run(() => OnConnectChanged?.Invoke(false));
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }

        /// <summary>
        /// 异步释放资源
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            _cts?.Cancel();
            _isRunning = false;

            await CleanupAsync().ConfigureAwait(false);

            _cts?.Dispose();
            _heartbeatCts?.Dispose();
            _sendLock?.Dispose();

            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// WebSocket客户端（简化版本，无重试机制）
    /// </summary>
    public class SimpleWebSocketClient : WebSocketClient, IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="url">WebSocket服务器地址</param>
        /// <param name="token">认证令牌（可选）</param>
        public SimpleWebSocketClient(string url, string token = null)
            : base(url, token)
        {
        }

        /// <summary>
        /// 异步释放资源
        /// </summary>
        public new async ValueTask DisposeAsync()
        {
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}