using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.Infrastructure.GameData;

/// <summary>只向英雄联盟 DX11 覆盖层发送展示数据；加载被拒绝后不重试或绕过保护。</summary>
public sealed class NativeGameOverlayBridge : IGameOverlayBridge
{
    internal const int PixelOffset = 65536, PixelCapacity = 32 * 1024 * 1024;
    internal const int AuxOffset = PixelOffset + PixelCapacity, BattlePixelOffset = AuxOffset + 65536;
    internal const int MappingSize = BattlePixelOffset + 1600 * 1000 * 4;
    internal const int TitleOffset = 128, StatusOffset = 384, RecommendationsOffset = 2432, OfferedOffset = 35200;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Session? _session;
    private Process? _loader;
    private long _nextLookup;
    private int _connecting, _attemptedPid;
    private bool _enabled, _disposed;
    private GameOverlayFrame? _lastFrame;
    private sealed record PanelPayload(GameOverlayPanel Panel, byte[]? Pixels, int Width, int Height);
    private readonly Dictionary<GameOverlayPanelKind, PanelPayload> _panels = new();
    private readonly System.Threading.Timer _heartbeat;
    private bool _panelsDirty, _frameDirty = true;
    private readonly HashSet<GameOverlayPanelKind> _dirtyPanels = [];
    private int _battleVersion;
    public NativeGameOverlayBridge() : this(true) { }
    internal NativeGameOverlayBridge(bool connectToGame) => _heartbeat = new(_ => Heartbeat(), null,
        connectToGame ? 250 : Timeout.Infinite, connectToGame ? 250 : Timeout.Infinite);

    private void Heartbeat()
    {
        lock (_gate)
        {
            if (_disposed) return;
            foreach (var kind in _panels.Where(p => p.Value.Panel.Deadline > 0 && p.Value.Panel.Deadline <= Environment.TickCount64).Select(p => p.Key).ToArray())
            { _panels.Remove(kind); _panelsDirty = true; }
            if ((_enabled || _panels.Count > 0) && Environment.TickCount64 >= _nextLookup && Interlocked.CompareExchange(ref _connecting, 1, 0) == 0)
            { _nextLookup = Environment.TickCount64 + 2000; _ = Task.Run(ConnectAsync); }
            Flush();
        }
    }

    public void PublishPanel(GameOverlayPanel panel)
    {
        byte[]? pixels = null;
        int width = panel.Image?.Width ?? 0, height = panel.Image?.Height ?? 0;
        if (panel.Image is { } image)
        {
            if (image.Width > 1600 || image.Height > 1000) throw new ArgumentOutOfRangeException(nameof(panel));
            using var copy = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(copy)) graphics.DrawImageUnscaled(image, 0, 0);
            var data = copy.LockBits(new Rectangle(Point.Empty, copy.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                pixels = new byte[copy.Width * copy.Height * 4];
                for (int row = 0; row < copy.Height; row++) Marshal.Copy(data.Scan0 + row * data.Stride, pixels, row * copy.Width * 4, copy.Width * 4);
            }
            finally { copy.UnlockBits(data); }
            panel = panel with { Image = null };
        }
        lock (_gate)
        {
            if (_disposed) return;
            if (pixels==null && _panels.TryGetValue(panel.Kind,out var existing) &&
                existing.Panel with { Items=null } == panel with { Items=null } &&
                (existing.Panel.Items ?? []).SequenceEqual(panel.Items ?? [])) return;
            _panels[panel.Kind] = new(panel, pixels, width, height); _panelsDirty = true; _dirtyPanels.Add(panel.Kind);
            if (panel.Kind == GameOverlayPanelKind.Battle) ++_battleVersion;
            Flush();
        }
    }

    public void HidePanel(GameOverlayPanelKind kind)
    {
        lock (_gate) { if (_disposed) return; if (_panels.Remove(kind)) { _panelsDirty=true; _dirtyPanels.Remove(kind); } Flush(); }
    }

    public GameOverlayPanelCommand? ReadPanelCommand(GameOverlayPanelKind kind)
    {
        lock (_gate)
        {
            if (_disposed || kind != GameOverlayPanelKind.SkinPicker || _session is not { Failed: false } session || !TryAcquire(session.Mutex)) return null;
            try
            {
                int action = session.View.ReadInt32(AuxOffset + 64);
                if (action == 0) return null;
                var result = new GameOverlayPanelCommand(action, session.View.ReadInt32(AuxOffset + 68), session.View.ReadInt32(AuxOffset + 72));
                session.View.Write(AuxOffset + 64, 0); return result;
            }
            finally { session.Mutex.ReleaseMutex(); }
        }
    }

    private void Flush()
    {
        if (_session is not { Failed: false } session || !TryAcquire(session.Mutex)) return;
        try
        {
            bool changed=_frameDirty || _panelsDirty;
            if(_frameDirty) { WriteFrame(session.View, _lastFrame ?? new("", "", "", "", 0, false, false), _enabled || _panels.Count > 0); _frameDirty=false; }
            else { session.View.Write(8, _enabled || _panels.Count>0 ? 1 : 0); session.View.Write(24, Environment.TickCount64); }
            session.View.Write(80, _enabled ? 1 : 0);
            if (_panelsDirty) { WritePanels(session.View); _panelsDirty = false; _dirtyPanels.Clear(); }
            if(changed) session.View.Write(96,unchecked(session.View.ReadInt32(96)+1));
        }
        finally { session.Mutex.ReleaseMutex(); }
    }

    private void WritePanels(MemoryMappedViewAccessor view)
    {
        int flags = 0;
        foreach (var (kind, payload) in _panels)
        {
            flags |= (int)kind;
            if(!_dirtyPanels.Contains(kind)) continue;
            var p = payload.Panel;
            switch (kind)
            {
                case GameOverlayPanelKind.Battle:
                    view.Write(AuxOffset + 4, _battleVersion); view.Write(AuxOffset + 8, payload.Width); view.Write(AuxOffset + 12, payload.Height);
                    if (payload.Pixels is { } pixels) view.WriteArray(BattlePixelOffset, pixels, 0, pixels.Length);
                    break;
                case GameOverlayPanelKind.Coaching:
                    view.Write(AuxOffset + 16, p.Deadline); view.Write(AuxOffset + 32, p.Position);
                    view.Write(AuxOffset + 36, p.OffsetX); view.Write(AuxOffset + 40, p.OffsetY);
                    WriteText(view, AuxOffset + 128, 256, p.Title); WriteText(view, AuxOffset + 384, 2048, p.Body); WriteText(view, AuxOffset + 2432, 1024, p.Detail);
                    break;
                case GameOverlayPanelKind.SkinNotice:
                    view.Write(AuxOffset + 24, p.Deadline); WriteText(view, AuxOffset + 3456, 1024, p.Body); break;
                case GameOverlayPanelKind.SkinPicker:
                    view.Write(AuxOffset + 44, p.Ready ? 1 : 0); view.Write(AuxOffset + 48, p.CanRefresh ? 1 : 0);
                    view.Write(AuxOffset + 52, p.Selected); view.Write(AuxOffset + 56, Math.Min(256, p.Items?.Count ?? 0)); view.Write(AuxOffset + 60, p.Version);
                    view.Write(AuxOffset + 76, Math.Clamp(p.Category, 0, 2));
                    view.Write(AuxOffset + 80, Math.Max(0, p.Page)); view.Write(AuxOffset + 84, Math.Max(1, p.PageCount));
                    WriteText(view, AuxOffset + 4480, 256, p.Title); WriteText(view, AuxOffset + 4736, 2048, p.Body);
                    for (int i = 0; i < Math.Min(256, p.Items?.Count ?? 0); i++) WriteText(view, AuxOffset + 6784 + i * 192, 192, p.Items![i]);
                    break;
            }
        }
        view.Write(AuxOffset, flags);
    }

    public int Publish(GameOverlayFrame frame)
    {
        lock (_gate)
        {
            if (_disposed) return 0;
            if(!_enabled) _frameDirty=true;
            _enabled = true;
            if(_lastFrame != frame) _frameDirty=true;
            _lastFrame = frame;
            if (_session is not { } session) return 0;
            if (session.Failed) return 0;
            bool acquired = TryAcquire(session.Mutex);
            if (!acquired) return 0;
            try
            {
                Flush();
                int command = session.View.ReadInt32(32);
                session.View.Write(32, 0);
                int status = session.View.ReadInt32(12);
                if (status == 2) RuntimeDiagnostics.Report("游戏覆盖层", "已渲染", "推荐已绘制到游戏 DX11 帧；鼠标移入面板即可操作");
                else if (status < 0) RuntimeDiagnostics.Report("游戏覆盖层", "失败", $"原生渲染状态 {status}，未激活桌面窗口");
                return command;
            }
            finally { session.Mutex.ReleaseMutex(); }
        }
    }

    private async Task ConnectAsync()
    {
        Session? connectingSession = null;
        try
        {
            using var game = FindGame();
            if (game == null) return;
            int pid = game.Id;
            lock (_gate)
            {
                if (_disposed || (!_enabled && _panels.Count == 0) || _attemptedPid == pid) return;
                _attemptedPid = pid;
            }
            string directory = Path.Combine(AppContext.BaseDirectory, "Tools", "GameOverlay");
            string host = Path.Combine(directory, "sc-game-overlay-host.exe");
            string dll = Path.Combine(directory, "sc-game-overlay.dll");
            if (!File.Exists(host) || !File.Exists(dll))
            {
                RuntimeDiagnostics.Report("游戏覆盖层", "未安装", "缺少 Tools/GameOverlay 原生模块，请运行 native/GameOverlay/build.ps1 后重新构建");
                return;
            }
            directory = GameOverlayRuntimeFiles.Prepare(directory);
            host = Path.Combine(directory, "sc-game-overlay-host.exe");
            ulong nonce = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)) | 1;
            string name = $"Local\\SC.MayhemOverlay.v2.{pid}.{Environment.ProcessId}.{nonce:x16}";
            var session = connectingSession = new Session(name, pid);
            lock (_gate)
            {
                if (_disposed || (!_enabled && _panels.Count == 0)) { session.Dispose(); return; }
                _session?.Dispose();
                _session = session;
                _frameDirty = true; _panelsDirty = true; _dirtyPanels.UnionWith(_panels.Keys); Flush();
            }
            var start = new ProcessStartInfo(host)
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.ArgumentList.Add(pid.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add(nonce.ToString("x16"));
            Process process;
            lock (_gate)
            {
                if (_disposed) return;
                process = Process.Start(start) ?? throw new InvalidOperationException("无法启动游戏覆盖层加载器");
                _loader = process;
            }
            using var loaderLease = process;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
                await process.WaitForExitAsync().ConfigureAwait(false);
                throw;
            }
            string detail = (await output.ConfigureAwait(false) + " " + await errors.ConfigureAwait(false)).Trim();
            if (process.ExitCode != 0)
            {
                lock (_gate) { if (_session == session) { session.Failed = true; session.View.Write(8, 0); } }
                RuntimeDiagnostics.Report("游戏覆盖层", "加载失败", $"退出码 {process.ExitCode}：{detail}；本局不再自动加载");
            }
            else RuntimeDiagnostics.Report("游戏覆盖层", "已接入", "等待游戏提交 DX11 帧");
        }
        catch (OperationCanceledException)
        {
            MarkFailed(connectingSession);
            if (!_lifetime.IsCancellationRequested) RuntimeDiagnostics.Report("游戏覆盖层", "加载超时", "加载器未在 12 秒内完成，本局不再自动加载");
        }
        catch (Exception ex) { MarkFailed(connectingSession); RuntimeDiagnostics.Report("游戏覆盖层", "加载失败", ex.Message); }
        finally { lock (_gate) { _loader = null; } Interlocked.Exchange(ref _connecting, 0); }
    }

    private void MarkFailed(Session? session)
    {
        lock (_gate)
        {
            if (session != null && _session == session) { session.Failed = true; session.View.Write(8, 0); }
        }
    }

    private static Process? FindGame()
    {
        Process? selected = null;
        foreach (var process in Process.GetProcessesByName("League of Legends"))
        {
            try
            {
                if (selected == null && !process.HasExited && process.MainWindowHandle != IntPtr.Zero)
                { selected = process; continue; }
            }
            catch (InvalidOperationException) { }
            process.Dispose();
        }
        return selected;
    }

    internal static void WriteFrame(MemoryMappedViewAccessor view, GameOverlayFrame frame, bool enabled)
    {
        view.Write(8, enabled ? 1 : 0);
        view.Write(24, Environment.TickCount64);
        view.Write(36, Math.Clamp(frame.Rarity, 0, 2));
        view.Write(40, frame.Collapsed ? 1 : 0);
        view.Write(44, frame.Scanning ? 1 : 0);
        view.Write(80, enabled ? 1 : 0);
        WriteText(view, TitleOffset, 256, frame.Title);
        WriteText(view, StatusOffset, 2048, frame.Status);
        WriteText(view, RecommendationsOffset, 32768, frame.Recommendations);
        WriteText(view, OfferedOffset, 16384, frame.Offered);
    }

    internal static void WriteText(MemoryMappedViewAccessor view, int offset, int capacity, string value)
    {
        var bytes = new byte[capacity];
        // Encoder.Convert stops at a complete UTF-8 character; never split Chinese text.
        Encoding.UTF8.GetEncoder().Convert(value.AsSpan(), bytes.AsSpan(0, capacity - 1), true, out _, out _, out _);
        view.WriteArray(offset, bytes, 0, bytes.Length);
    }

    private static bool TryAcquire(Mutex mutex)
    {
        try { return mutex.WaitOne(0); }
        catch (AbandonedMutexException) { return true; }
    }

    public void Disable()
    {
        lock (_gate)
        {
            if(_enabled) _frameDirty=true;
            _enabled = false;
            if (!_disposed) Flush();
        }
    }

    public async Task<GameOverlayCapture?> CaptureAsync(CancellationToken cancellationToken = default)
    {
        await _captureGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Session session;
            int request;
            lock (_gate)
            {
                if (_disposed || !_enabled || _session == null || _session.View.ReadInt32(12) != 2) return null;
                session = _session;
                request = session.View.ReadInt32(48) + 1;
                if (request <= 0) request = 1;
                session.View.Write(48, request);
            }
            long deadline = Environment.TickCount64 + 2500;
            while (Environment.TickCount64 < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[]? pixels = null; int width = 0, height = 0; Size gameSize = default;
                lock (_gate)
                {
                    if (_disposed || !_enabled || _session != session) return null;
                    if (session.View.ReadInt32(52) == request)
                    {
                        Thread.MemoryBarrier();
                        int error = session.View.ReadInt32(72);
                        if (error < 0) throw new InvalidOperationException($"游戏帧取图失败：0x{error:x8}");
                        width = session.View.ReadInt32(56); height = session.View.ReadInt32(60);
                        gameSize = new Size(session.View.ReadInt32(64), session.View.ReadInt32(68));
                        if (width <= 0 || height <= 0 || width > 8192 || height > 8192 || (long)width * height * 4 > PixelCapacity)
                            throw new InvalidOperationException("游戏帧尺寸无效");
                        pixels = new byte[width * height * 4];
                        session.View.ReadArray(PixelOffset, pixels, 0, pixels.Length);
                    }
                }
                if (pixels != null)
                {
                    var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                    try
                    {
                        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                        try { for (int row = 0; row < height; row++) Marshal.Copy(pixels, row * width * 4, data.Scan0 + row * data.Stride, width * 4); }
                        finally { bitmap.UnlockBits(data); }
                        return new GameOverlayCapture(bitmap, gameSize);
                    }
                    catch { bitmap.Dispose(); throw; }
                }
                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }
            throw new TimeoutException("游戏帧取图超时，请保持游戏在前台");
        }
        finally { _captureGate.Release(); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; _enabled = false;
            _heartbeat.Dispose();
            _lifetime.Cancel();
            try { if (_loader is { HasExited: false }) _loader.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception ex) { RuntimeDiagnostics.WriteException(ex); }
            _panels.Clear(); _lastFrame = null;
            _session?.Dispose(); _session = null;
        }
    }

    private sealed class Session : IDisposable
    {
        private readonly MemoryMappedFile _mapping;
        public MemoryMappedViewAccessor View { get; }
        public Mutex Mutex { get; }
        public bool Failed { get; set; }
        public Session(string name, int pid)
        {
            Mutex = new Mutex(false, name + ".Lock");
            MemoryMappedFile? mapping = null;
            MemoryMappedViewAccessor? view = null;
            try
            {
                mapping = MemoryMappedFile.CreateNew(name, MappingSize, MemoryMappedFileAccess.ReadWrite);
                view = mapping.CreateViewAccessor();
                view.Write(0, 0x53434f56u); view.Write(4, 2u);
                view.Write(16, Environment.ProcessId); view.Write(20, pid);
                _mapping = mapping; View = view;
            }
            catch { view?.Dispose(); mapping?.Dispose(); Mutex.Dispose(); throw; }
        }
        public void Dispose() { View.Write(8, 0); View.Dispose(); _mapping.Dispose(); Mutex.Dispose(); }
    }
}
