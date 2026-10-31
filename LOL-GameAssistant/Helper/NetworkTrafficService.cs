using System.Net;
using System.Text.Json;

namespace LOL_GameAssistant.Helper;

public enum NetworkModule { Assets, Builds, Augments, Patch, Ranked, Ai, Updates, Rewards, Identity, Lounge, Other, Lcu, Matches, LiveGame, ClientEvents, ClientRpc, LocalAssets }
public sealed record NetworkModuleInfo(NetworkModule Module, string Name, string Description, bool Local = false);
public sealed record TrafficSnapshot(NetworkModuleInfo Info, bool Enabled, long Requests, long Blocked, long Uploaded, long Downloaded);

/// <summary>Counts application payload, excluding protocol headers. Never retains URLs or credentials.</summary>
public sealed class NetworkTrafficService
{
    public static NetworkTrafficService Shared { get; } = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LOL-GameAssistant", "network-access.json"));
    public static IReadOnlyList<NetworkModuleInfo> Modules { get; } = new NetworkModuleInfo[]
    {
        new(NetworkModule.Assets, "图片与游戏资源", "英雄、装备、符文图片及静态数据；本地缓存仍可使用"),
        new(NetworkModule.Builds, "出装与英雄统计", "OP.GG、ARAMKit、aramgg 推荐与胜率数据"),
        new(NetworkModule.Augments, "增幅数据更新", "增幅对照表自动更新及增幅图标"),
        new(NetworkModule.Patch, "版本资源检查", "Data Dragon 版本和资源变更检查"),
        new(NetworkModule.Ranked, "跨区战绩查询", "SGP 跨区战绩、段位与玩家查询"),
        new(NetworkModule.Ai, "云端 AI", "AI 建议、模型列表和连接测试"),
        new(NetworkModule.Updates, "软件更新", "检查新版本及下载更新包"),
        new(NetworkModule.Rewards, "奖励与魄罗商城", "腾讯活动、签到、奖励领取和商城接口"),
        new(NetworkModule.Identity, "个性化预览", "客户端边框等个性化预览资源"),
        new(NetworkModule.Lounge, "lol 休息室", "聊天室连接、消息、在线状态与同步"),
        new(NetworkModule.Other, "其他公网访问", "共享请求入口中未归类的公网访问"),
        new(NetworkModule.Lcu, "客户端基础通信", "本机 LCU 状态及客户端操作；关闭会影响基础功能", true),
        new(NetworkModule.LocalAssets, "客户端本地资源", "通过本机客户端读取英雄、装备、符文静态资源", true),
        new(NetworkModule.Matches, "本机战绩查询", "通过本机客户端读取战绩，客户端可能继续向服务器查询", true),
        new(NetworkModule.LiveGame, "实时对局数据", "本机 2999 对局数据接口", true),
        new(NetworkModule.ClientEvents, "客户端事件订阅", "本机 WebSocket 事件；关闭会影响状态自动刷新", true),
        new(NetworkModule.ClientRpc, "客户端奖励通信", "本机腾讯客户端 RPC；关闭会影响奖励及商城功能", true)
    };
    private sealed class State
    {
        internal bool Enabled = true;
        internal long Requests, Blocked, Uploaded, Downloaded;
        internal readonly HashSet<CancellationTokenSource> Active = [];
    }
    private readonly object _gate = new();
    private readonly Dictionary<NetworkModule, State> _states = Enum.GetValues<NetworkModule>().ToDictionary(m => m, _ => new State());
    private readonly string? _path;
    public NetworkTrafficService(string? path = null)
    {
        _path = path;
        if (path == null || !File.Exists(path)) return;
        try { foreach (var module in JsonSerializer.Deserialize<NetworkModule[]>(File.ReadAllText(path)) ?? []) if (_states.TryGetValue(module, out var state)) state.Enabled = false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { System.Diagnostics.Debug.WriteLine(ex.Message); }
    }
    public bool IsEnabled(NetworkModule module) { lock (_gate) return _states[module].Enabled; }
    public void SetEnabled(NetworkModule module, bool enabled)
    {
        CancellationTokenSource[] active;
        lock (_gate)
        {
            var state = _states[module];
            if (state.Enabled == enabled) return;
            if (_path != null)
            {
                var disabled = _states.Where(p => p.Key == module ? !enabled : !p.Value.Enabled).Select(p => p.Key).ToArray();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
                string temporary = _path + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(disabled));
                File.Move(temporary, _path, true);
            }
            state.Enabled = enabled;
            active = enabled ? [] : state.Active.ToArray();
        }
        foreach (var source in active) try { source.Cancel(); } catch (ObjectDisposedException) { }
    }
    public TrafficSnapshot[] Snapshot()
    {
        lock (_gate) return Modules.Select(info => { var s = _states[info.Module]; return new TrafficSnapshot(info, s.Enabled, s.Requests, s.Blocked, s.Uploaded, s.Downloaded); }).ToArray();
    }
    public void ResetCounters() { lock (_gate) foreach (var s in _states.Values) s.Requests = s.Blocked = s.Uploaded = s.Downloaded = 0; }
    internal void AddBytes(NetworkModule module, int count, bool upload) { lock (_gate) { if (upload) _states[module].Uploaded += count; else _states[module].Downloaded += count; } }
    internal Lease Begin(NetworkModule module, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var state = _states[module];
            if (!state.Enabled) { state.Blocked++; throw new HttpRequestException("该模块的网络访问已在「日志 → 流量监控」中关闭。"); }
            var source = CancellationTokenSource.CreateLinkedTokenSource(token);
            state.Active.Add(source); state.Requests++;
            return new Lease(this, module, source);
        }
    }
    internal sealed class Lease(NetworkTrafficService owner, NetworkModule module, CancellationTokenSource source) : IDisposable
    {
        private int _disposed;
        internal CancellationToken Token => source.Token;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) != 0) return; lock (owner._gate) owner._states[module].Active.Remove(source); source.Dispose(); }
    }
    public static HttpMessageHandler Handler(NetworkModule? module = null, HttpMessageHandler? inner = null) => new TrafficHandler(Shared, module, inner ?? new HttpClientHandler());
    internal static NetworkModule Classify(Uri? uri)
    {
        if (uri == null) return NetworkModule.Other;
        if (uri.IsLoopback)
        {
            if (uri.Port == 2999) return NetworkModule.LiveGame;
            if (uri.AbsolutePath.StartsWith("/lol-match-history/", StringComparison.OrdinalIgnoreCase)) return NetworkModule.Matches;
            if (uri.AbsolutePath.StartsWith("/lol-game-data/", StringComparison.OrdinalIgnoreCase)) return NetworkModule.LocalAssets;
            return NetworkModule.Lcu;
        }
        if (uri.Host.Equals("ddragon.leagueoflegends.com", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath == "/api/versions.json") return NetworkModule.Patch;
        if (uri.Host.EndsWith("dragon.leagueoflegends.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("communitydragon.org", StringComparison.OrdinalIgnoreCase)) return NetworkModule.Assets;
        return NetworkModule.Other;
    }
    internal sealed class TrafficHandler(NetworkTrafficService owner, NetworkModule? module, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var category = module ?? Classify(request.RequestUri);
            var lease = owner.Begin(category, cancellationToken);
            try
            {
                if (request.Content != null) request.Content = new TrafficContent(request.Content, owner, category, true, lease.Token);
                var response = await base.SendAsync(request, lease.Token).ConfigureAwait(false);
                response.Content = new TrafficContent(response.Content, owner, category, false, lease.Token, lease);
                return response;
            }
            catch { lease.Dispose(); throw; }
        }
    }
    private sealed class TrafficContent : HttpContent
    {
        private readonly HttpContent _inner;
        private readonly NetworkTrafficService _owner;
        private readonly NetworkModule _module;
        private readonly bool _upload;
        private readonly CancellationToken _token;
        private readonly Lease? _lease;
        internal TrafficContent(HttpContent inner, NetworkTrafficService owner, NetworkModule module, bool upload, CancellationToken token, Lease? lease = null)
        {
            _inner = inner; _owner = owner; _module = module; _upload = upload; _token = token; _lease = lease;
            foreach (var header in inner.Headers) Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        protected override bool TryComputeLength(out long length) { length = _inner.Headers.ContentLength ?? -1; return length >= 0; }
        protected override Task<Stream> CreateContentReadStreamAsync() => CreateContentReadStreamAsync(_token);
        protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) => new TrafficStream(await _inner.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), _owner, _module, _upload, _token);
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => SerializeToStreamAsync(stream, context, _token);
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _token);
            var source = await CreateContentReadStreamAsync(linked.Token).ConfigureAwait(false);
            await source.CopyToAsync(stream, linked.Token).ConfigureAwait(false);
        }
        protected override void Dispose(bool disposing) { if (disposing) { _inner.Dispose(); _lease?.Dispose(); } base.Dispose(disposing); }
    }
    private sealed class TrafficStream(Stream inner, NetworkTrafficService owner, NetworkModule module, bool upload, CancellationToken token) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) { token.ThrowIfCancellationRequested(); int read = inner.Read(buffer, offset, count); owner.AddBytes(module, read, upload); return read; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, token);
            int read = await inner.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
            owner.AddBytes(module, read, upload); return read;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
