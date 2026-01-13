using System;
using System.Buffers;  // 引入ArrayPool
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LOL_GameAssistant.Helper
{
    /// <summary>
    /// WebSocket客户端，支持认证、心跳和自动重连
    /// </summary>
    public class WebSocketClient : IDisposable
    {
        private ClientWebSocket _socket;
        private CancellationTokenSource _cts;
        private readonly string _url;
        private readonly string _token;
        private bool _isRunning;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1); // 发送锁，确保线程安全
        private CancellationTokenSource _heartbeatCts; // 心跳任务的取消令牌

        /// <summary>
        /// 当前连接状态
        /// </summary>
        public bool IsConnected => _socket?.State == WebSocketState.Open;

        /// <summary>
        /// 收到消息时触发
        /// </summary>
        public event Action<string> OnMessage;

        /// <summary>
        /// 发生错误时触发
        /// </summary>
        public event Action<string> OnError;

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
            // 检查是否已经在运行
            if (_isRunning)
                throw new InvalidOperationException("客户端已经在运行");

            // 清理之前的连接
            _socket?.Dispose();
            _socket = new ClientWebSocket();

            // 添加认证头（Basic认证）
            if (!string.IsNullOrEmpty(_token))
            {
                _socket.Options.SetRequestHeader("Authorization", $"Basic {_token}");
            }

#if DEBUG
            // 仅在开发环境跳过SSL验证
            _socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
#else
            // 生产环境使用系统默认验证
            _socket.Options.RemoteCertificateValidationCallback = null;
#endif

            try
            {
                // 连接到服务器
                await _socket.ConnectAsync(new Uri(_url), _cts.Token);
                _isRunning = true;

                // 触发连接状态变化事件
                OnConnectChanged?.Invoke(true);

                // 启动接收循环（不等待）
                _ = Task.Run(() => ReceiveLoopAsync(), _cts.Token);

                // 启动心跳任务
                _heartbeatCts = new CancellationTokenSource();
                _ = Task.Run(() => HeartbeatLoopAsync(), _heartbeatCts.Token);
            }
            catch (Exception ex)
            {
                // 触发错误事件
                OnError?.Invoke($"连接失败: {ex.Message}");
                _isRunning = false;
                throw;
            }
        }

        /// <summary>
        /// 发送消息到服务器
        /// </summary>
        /// <param name="message">要发送的消息</param>
        /// <exception cref="InvalidOperationException">未连接</exception>
        public async Task SendAsync(string message)
        {
            if (!IsConnected)
                throw new InvalidOperationException("未连接到服务器");

            // 使用信号量确保同一时间只有一个发送操作
            await _sendLock.WaitAsync(_cts.Token);
            try
            {
                // 将消息转换为UTF-8字节数组
                var buffer = Encoding.UTF8.GetBytes(message);

                // 发送消息，标记为文本类型，且是结束帧
                await _socket.SendAsync(
                    new ArraySegment<byte>(buffer),
                    WebSocketMessageType.Text,
                    true,  // 结束帧标记
                    _cts.Token
                );
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
            // 使用ArrayPool租用内存，减少GC压力
            using var memoryOwner = MemoryPool<byte>.Shared.Rent(4096);
            var buffer = memoryOwner.Memory;
            var segments = new List<ArraySegment<byte>>(); // 用于存储分片消息

            while (_isRunning && IsConnected && !_cts.Token.IsCancellationRequested)
            {
                try
                {
                    // 接收消息
                    var result = await _socket.ReceiveAsync(buffer, _cts.Token);

                    // 如果是关闭消息，则断开连接
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await CloseAsync();
                        break;
                    }

                    // 将接收到的数据添加到列表中
                    segments.Add(new ArraySegment<byte>(buffer.Slice(0, result.Count).ToArray()));

                    // 如果消息未结束，继续接收
                    if (!result.EndOfMessage)
                    {
                        continue;
                    }

                    // 拼接完整消息
                    var totalBytes = segments.Sum(s => s.Count);
                    var messageBytes = new byte[totalBytes];
                    var offset = 0;

                    foreach (var segment in segments)
                    {
                        Buffer.BlockCopy(segment.Array, segment.Offset, messageBytes, offset, segment.Count);
                        offset += segment.Count;
                    }

                    // 清空分片列表
                    segments.Clear();

                    // 解码消息
                    var message = Encoding.UTF8.GetString(messageBytes);

                    // 触发消息接收事件
                    OnMessage?.Invoke(message);

                    // 处理心跳消息
                    if (message == "PING")
                    {
                        await SendAsync("PONG");
                    }
                }
                catch (OperationCanceledException)
                {
                    // 正常取消，不记录错误
                    break;
                }
                catch (Exception ex)
                {
                    // 如果仍在运行状态，触发错误事件
                    if (_isRunning)
                    {
                        OnError?.Invoke($"接收错误: {ex.Message}");
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
            while (_isRunning && IsConnected && !_heartbeatCts.Token.IsCancellationRequested)
            {
                try
                {
                    // 每30秒发送一次心跳
                    await Task.Delay(TimeSpan.FromSeconds(30), _heartbeatCts.Token);

                    if (IsConnected && !_heartbeatCts.Token.IsCancellationRequested)
                    {
                        await SendAsync("PING");
                    }
                }
                catch (OperationCanceledException)
                {
                    // 心跳任务被取消，正常退出
                    break;
                }
                catch (Exception ex)
                {
                    // 记录心跳错误但不中断循环
                    OnError?.Invoke($"心跳错误: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 关闭WebSocket连接
        /// </summary>
        public async Task CloseAsync()
        {
            // 标记为非运行状态
            _isRunning = false;

            // 停止心跳任务
            _heartbeatCts?.Cancel();
            _heartbeatCts?.Dispose();
            _heartbeatCts = null;

            // 如果已连接，发送关闭消息
            if (IsConnected)
            {
                try
                {
                    await _socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "客户端主动关闭",
                        CancellationToken.None
                    );
                }
                catch (Exception ex)
                {
                    OnError?.Invoke($"关闭连接时出错: {ex.Message}");
                }
            }

            // 触发连接状态变化事件
            OnConnectChanged?.Invoke(false);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            // 停止运行
            _isRunning = false;

            // 取消所有异步操作
            _cts?.Cancel();
            _heartbeatCts?.Cancel();

            // 关闭并释放WebSocket
            try
            {
                if (IsConnected)
                {
                    _socket?.CloseAsync(WebSocketCloseStatus.NormalClosure, "释放资源", CancellationToken.None).Wait(1000);
                }
            }
            catch
            {
                // 忽略关闭过程中的错误
            }

            // 释放所有资源
            _socket?.Dispose();
            _cts?.Dispose();
            _heartbeatCts?.Dispose();
            _sendLock?.Dispose();

            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// 具有自动重连功能的WebSocket客户端
    /// </summary>
    public class ResilientWebSocketClient : WebSocketClient
    {
        private int _retryCount = 0;
        private readonly RetryPolicy _retryPolicy;

        /// <summary>
        /// 重连策略
        /// </summary>
        public class RetryPolicy
        {
            /// <summary>
            /// 最大重试次数
            /// </summary>
            public int MaxRetries { get; set; } = 3;

            /// <summary>
            /// 初始延迟时间（秒）
            /// </summary>
            public int InitialDelaySeconds { get; set; } = 1;

            /// <summary>
            /// 最大延迟时间（秒）
            /// </summary>
            public int MaxDelaySeconds { get; set; } = 10;

            /// <summary>
            /// 是否使用指数退避
            /// </summary>
            public bool UseExponentialBackoff { get; set; } = true;
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="url">WebSocket服务器地址</param>
        /// <param name="token">认证令牌（可选）</param>
        /// <param name="retryPolicy">重连策略（可选）</param>
        public ResilientWebSocketClient(string url, string token = null, RetryPolicy retryPolicy = null)
            : base(url, token)
        {
            _retryPolicy = retryPolicy ?? new RetryPolicy();

            // 监听连接状态变化，用于重置重试计数
            this.OnConnectChanged += connected =>
            {
                if (connected)
                {
                    _retryCount = 0; // 连接成功时重置重试计数
                }
            };
        }

        /// <summary>
        /// 带重试机制的连接方法
        /// </summary>
        /// <returns>连接成功返回true，超过重试次数返回false</returns>
        public async Task<bool> ConnectWithRetryAsync()
        {
            _retryCount = 0;

            while (_retryCount < _retryPolicy.MaxRetries)
            {
                try
                {
                    await ConnectAsync();
                    return true; // 连接成功
                }
                catch (Exception ex)
                {
                    _retryCount++;

                    // 如果是最后一次重试，抛出异常
                    if (_retryCount >= _retryPolicy.MaxRetries)
                    {
                        //OnError?.Invoke($"连接失败，已达最大重试次数({_retryPolicy.MaxRetries}): {ex.Message}");
                        return false;
                    }

                    // 计算延迟时间
                    int delaySeconds;
                    if (_retryPolicy.UseExponentialBackoff)
                    {
                        // 指数退避：1, 2, 4, 8... 秒，但不超过最大延迟
                        delaySeconds = Math.Min(
                            _retryPolicy.InitialDelaySeconds * (int)Math.Pow(2, _retryCount - 1),
                            _retryPolicy.MaxDelaySeconds
                        );
                    }
                    else
                    {
                        // 固定延迟
                        delaySeconds = _retryPolicy.InitialDelaySeconds;
                    }

                    // 触发重试事件
                    //OnError?.Invoke($"连接失败，{delaySeconds}秒后第{_retryCount}次重试: {ex.Message}");

                    // 等待一段时间后重试
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                }
            }

            return false;
        }

        /// <summary>
        /// 强制重新连接
        /// </summary>
        public async Task ReconnectAsync()
        {
            // 先关闭现有连接
            await CloseAsync();

            // 重置重试计数并重新连接
            _retryCount = 0;
            await ConnectWithRetryAsync();
        }
    }
}