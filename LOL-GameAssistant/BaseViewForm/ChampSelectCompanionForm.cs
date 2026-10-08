using System.Diagnostics;
using System.Runtime.InteropServices;
using LOL_GameAssistant.Application.ChampionSelect;
using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Lobby;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.ChampionSelect;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Ranked;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>选人阶段的伴随窗，跟随 LeagueClientUx 窗口移动、缩放和最小化。</summary>
internal sealed class ChampSelectCompanionForm : AntdUI.Window
{
    private readonly IChampionSelectService _selection;
    private readonly ILobbyService _lobby;
    private readonly IClientFeatureService _features;
    private readonly IChampionCatalog _champions;
    private readonly IPlayerProfileService _players;
    private readonly IRankedStatsService _ranked;
    private readonly IMatchHistoryService _matches;
    private readonly IApplicationSettingsStore _settings;
    private readonly Func<Rectangle?> _clientBounds;
    private IntPtr _clientWindow;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 150 };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly FlowLayoutPanel _cards = new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
        WrapContents = false, AutoScroll = true, Padding = new Padding(9)
    };
    private readonly Button _chooseBuild = new()
    {
        Dock = DockStyle.Right, Width = 132, Text = "一键选择符文装备",
        FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(53, 66, 90), ForeColor = Color.White
    };
    private readonly Label _buildStatus = new()
    {
        Dock = DockStyle.Bottom, Height = 36, Visible = false,
        ForeColor = Color.LightGray, AutoEllipsis = true, Padding = new Padding(7, 3, 7, 0)
    };
    private readonly AntdUI.Label _automation = new() { Dock = DockStyle.Bottom, Height = 44, ForeColor = Color.LightGray };
    private readonly Panel _mayhemBench = new() { Dock = DockStyle.Bottom, Height = 150, Visible = false, Padding = new Padding(7, 4, 7, 4) };
    private readonly Label _benchStatus = new() { Dock = DockStyle.Top, Height = 38, ForeColor = Color.White };
    private readonly FlowLayoutPanel _benchChoices = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true };
    private string _lastBench = "<unloaded>";
    private string _benchMessage = "";
    private bool _swapping;
    private bool _tracking;
    private bool _loading;
    private bool _selectionLoading;
    private bool _modeLoading;
    private bool? _isMayhem;
    private bool _isAram;
    private ChampionSelectionSnapshot? _lastSelection;
    private CancellationTokenSource? _trackingCancellation;
    private int _trackingGeneration;
    private int _selectionRevision;
    private DateTimeOffset _lastAllyRefresh;
    private DateTimeOffset _nextModeRefresh;
    private string _lastRoster = "";
    private DateTimeOffset _lastRefresh;
    private DateTimeOffset _lastRosterDataAt;
    private int _languageRevision;

    /// <summary>初始化 ChampSelectCompanionForm 的实例状态。</summary>
    public ChampSelectCompanionForm() : this(
        AppCompositionRoot.ChampionSelectService, AppCompositionRoot.LobbyService,
        AppCompositionRoot.ClientFeatureService, AppCompositionRoot.ChampionCatalog,
        AppCompositionRoot.PlayerProfileService, AppCompositionRoot.RankedStatsService,
        AppCompositionRoot.MatchHistoryService, AppCompositionRoot.ApplicationSettingsStore)
    { }

    /// <summary>初始化 ChampSelectCompanionForm 的实例状态，并保存传入的依赖或数据。</summary>
    internal ChampSelectCompanionForm(IChampionSelectService selection, ILobbyService lobby,
        IClientFeatureService features, IChampionCatalog champions, IPlayerProfileService players,
        IRankedStatsService ranked, IMatchHistoryService matches, IApplicationSettingsStore settings,
        Func<Rectangle?>? clientBounds = null)
    {
        _selection = selection;
        _lobby = lobby;
        _features = features;
        _champions = champions;
        _players = players;
        _ranked = ranked;
        _matches = matches;
        _settings = settings;
        _clientBounds = clientBounds ?? (() => TryGetClientBounds(out var bounds) ? bounds : null);
        Text = "LOL 选人伴随窗";
        AntdWindowChrome.Configure(this);
        EnableHitTest = false;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(22, 27, 39);
        ForeColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9);
        Size = new Size(280, 440);
        _mayhemBench.BackColor = Color.FromArgb(31, 39, 56);
        _mayhemBench.Controls.Add(_benchChoices);
        _mayhemBench.Controls.Add(_benchStatus);
        Controls.Add(_cards);
        Controls.Add(_mayhemBench);
        Controls.Add(_automation);
        Controls.Add(_buildStatus);
        var header = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Color.FromArgb(31, 39, 56) };
        header.Controls.Add(new AntdUI.Label
        {
            Text = "队友 · 近 20 场", Dock = DockStyle.Fill,
            ForeColor = Color.White, TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font, FontStyle.Bold)
        });
        header.Controls.Add(_chooseBuild);
        Controls.Add(header);
        _chooseBuild.Click += async (_, _) => await ChooseBuildAsync();
        _timer.Tick += async (_, _) => await RefreshAsync();
        UiLanguage.Changed += LanguageChanged;
        Disposed += (_, _) =>
        {
            _lifetime.Cancel();
            _trackingCancellation?.Cancel();
            _trackingCancellation?.Dispose();
            UiLanguage.Changed -= LanguageChanged;
            _timer.Dispose();
            _lifetime.Dispose();
        };
    }

    /// <summary>显示浮窗时不抢占游戏或客户端的输入焦点。</summary>
    protected override bool ShowWithoutActivation => true;
    /// <summary>保留无激活或分层透明等浮窗需要的原生窗口标志。</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            return parameters;
        }
    }

    /// <summary>启动当前对象的持续跟踪流程。</summary>
    public void StartTracking()
    {
        if (IsDisposed || _tracking) return;
        _trackingGeneration++;
        _trackingCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _loading = _selectionLoading = _modeLoading = _swapping = false;
        _isMayhem = null;
        _isAram = false;
        _lastSelection = null;
        _lastRefresh = _lastAllyRefresh = _nextModeRefresh = DateTimeOffset.MinValue;
        _tracking = true;
        _timer.Start();
        _ = RefreshSelectionAsync();
        _ = ResolveBenchModeAsync();
        _ = RefreshAsync();
    }

    /// <summary>停止持续跟踪并清理未完成的工作。</summary>
    public void StopTracking()
    {
        if (IsDisposed) return;
        _tracking = false;
        _trackingGeneration++;
        _trackingCancellation?.Cancel();
        _trackingCancellation?.Dispose();
        _trackingCancellation = null;
        _lastSelection = null;
        _timer.Stop();
        _lastRoster = "";
        _lastBench = "<unloaded>";
        _benchMessage = "";
        _mayhemBench.Visible = false;
        ControlLifetime.ClearAndDispose(_benchChoices);
        _buildStatus.Visible = false;
        _lastRosterDataAt = DateTimeOffset.MinValue;
        Hide();
    }

    /// <summary>语言变化后刷新依赖本地化文本的界面。</summary>
    private void LanguageChanged(object? sender, EventArgs args)
    {
        _languageRevision++;
        _lastRoster = "";
        _lastBench = "<unloaded>";
        _benchMessage = "";
        _chooseBuild.Text = UiLanguage.IsEnglish ? "Runes & items" : "一键选择符文装备";
        _buildStatus.Visible = false;
        _lastRefresh = DateTimeOffset.MinValue;
        if (_lastSelection != null) RenderMayhemBench(_lastSelection);
        if (_tracking) _ = RefreshAsync();
    }

    /// <summary>异步刷新当前服务或界面负责的数据。</summary>
    private async Task RefreshAsync()
    {
        if (!SyncClientVisibility()) return;
        if (!_modeLoading && _isMayhem == null && DateTimeOffset.UtcNow >= _nextModeRefresh)
            _ = ResolveBenchModeAsync();
        if (_lastSelection != null) _ = RefreshAlliesAsync(_lastSelection);
        await RefreshSelectionAsync();
    }

    /// <summary>读取最新选人状态并更新选人相关内容。</summary>
    private async Task RefreshSelectionAsync()
    {
        if (!_tracking || IsDisposed) return;
        if (_selectionLoading) return;
        if (DateTimeOffset.UtcNow - _lastRefresh < TimeSpan.FromMilliseconds(500)) return;
        _lastRefresh = DateTimeOffset.UtcNow;
        _selectionLoading = true;
        int generation = _trackingGeneration;
        int revision = _selectionRevision;
        CancellationToken token = _trackingCancellation!.Token;
        try
        {
            ChampionSelectionSnapshot? selection = await _selection.GetSessionAsync(token);
            if (!IsCurrentTracking(generation) || token.IsCancellationRequested || revision != _selectionRevision) return;
            if (selection != null) ObserveChampionSelection(selection);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (IsCurrentTracking(generation)) RuntimeDiagnostics.Report("备战席英雄", "读取失败", ex.Message);
        }
        finally { if (generation == _trackingGeneration) _selectionLoading = false; }
    }

    /// <summary>检查当前请求是否仍属于有效的跟踪周期。</summary>
    private bool IsCurrentTracking(int generation) => _tracking && !IsDisposed && generation == _trackingGeneration;

    /// <summary>选人推送到达后先更新按钮；队友资料与模式查询都不阻塞备战席。</summary>
    public void ObserveChampionSelection(ChampionSelectionSnapshot selection)
    {
        if (!_tracking || IsDisposed) return;
        _selectionRevision++;
        _lastSelection = selection;
        RenderMayhemBench(selection);
        _ = RefreshAlliesAsync(selection);
    }

    /// <summary>读取队友信息并更新队友卡片。</summary>
    private async Task RefreshAlliesAsync(ChampionSelectionSnapshot selection)
    {
        if (!_tracking || IsDisposed || _loading ||
            DateTimeOffset.UtcNow - _lastAllyRefresh < TimeSpan.FromSeconds(4)) return;
        _lastAllyRefresh = DateTimeOffset.UtcNow;
        _loading = true;
        int languageRevision = _languageRevision;
        int generation = _trackingGeneration;
        try
        {
            string roster = string.Join("|", selection.MyTeam.Select(member => member.Puuid));
            if (roster == _lastRoster && _cards.Controls.Count > 0 &&
                DateTimeOffset.UtcNow - _lastRosterDataAt < TimeSpan.FromSeconds(30)) return;
            _lastRoster = roster;
            var config = _settings.Load();
            _automation.Text = UiLanguage.IsEnglish
                ? $"Auto ban: {(config.AutoBan ? string.Join(", ", config.BanChampions.Take(2)) : "Off")}\n" +
                  $"Auto pick: {(config.AutoPick ? string.Join(", ", config.PickChampions.Take(2)) : "Off")}"
                : $"自动禁用：{(config.AutoBan ? string.Join("、", config.BanChampions.Take(2)) : "关闭")}\n" +
                  $"自动选用：{(config.AutoPick ? string.Join("、", config.PickChampions.Take(2)) : "关闭")}";
            var allies = selection.MyTeam
                .Where(member => member.CellId != selection.LocalPlayerCellId)
                .Take(4).ToArray();
            var summaries = await Task.WhenAll(allies.Select(LoadAllyAsync));
            if (!IsCurrentTracking(generation) || roster != _lastRoster || languageRevision != _languageRevision) return;
            _lastRosterDataAt = DateTimeOffset.UtcNow;
            _cards.SuspendLayout();
            try
            {
                ControlLifetime.ClearAndDispose(_cards);
                foreach (string summary in summaries)
                {
                    _cards.Controls.Add(new AntdUI.Label
                    {
                        Text = summary, Width = Math.Max(200, _cards.ClientSize.Width - 20),
                        Height = 82, Padding = new Padding(8), Margin = new Padding(0, 0, 0, 5),
                        BackColor = Color.FromArgb(38, 47, 67), ForeColor = Color.White
                    });
                }
            }
            finally { _cards.ResumeLayout(); }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { if (IsCurrentTracking(generation)) _automation.Text = UiLanguage.T($"队友数据暂不可用：{ex.Message}"); }
        finally { if (generation == _trackingGeneration) _loading = false; }
    }

    /// <summary>确认当前队列是否支持大乱斗备选英雄功能。</summary>
    private async Task ResolveBenchModeAsync()
    {
        _modeLoading = true;
        int generation = _trackingGeneration;
        CancellationToken token = _trackingCancellation!.Token;
        try
        {
            // 并发读取，且每个选人阶段只在模式尚未确定时查询。
            var lobbyTask = _lobby.GetLobbyAsync(token);
            var gameTask = _lobby.GetCurrentSessionAsync(token);
            await Task.WhenAll(lobbyTask, gameTask);
            if (!IsCurrentTracking(generation) || token.IsCancellationRequested) return;
            LobbySnapshot? lobby = await lobbyTask;
            ActiveGameSnapshot? game = await gameTask;
            int queueId = game?.QueueId is > 0 ? game.QueueId : lobby?.QueueId ?? 0;
            string mode = !string.IsNullOrWhiteSpace(game?.GameMode) ? game.GameMode : lobby?.GameMode ?? "";
            if (queueId == 0 && string.IsNullOrWhiteSpace(mode)) return;
            _isMayhem = queueId is 2400 or 3270 || mode.StartsWith("KIWI", StringComparison.OrdinalIgnoreCase);
            _isAram = queueId == 450 || string.Equals(mode, "ARAM", StringComparison.OrdinalIgnoreCase);
            _lastBench = "<unloaded>";
            if (_lastSelection != null) RenderMayhemBench(_lastSelection);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (IsCurrentTracking(generation)) RuntimeDiagnostics.Report("备战席模式", "等待模式信息", ex.Message);
        }
        finally
        {
            if (generation == _trackingGeneration)
            {
                _modeLoading = false;
                _nextModeRefresh = DateTimeOffset.UtcNow.AddSeconds(2);
            }
        }
    }

    /// <summary>展示海克斯大乱斗备选英雄及交换状态。</summary>
    private void RenderMayhemBench(ChampionSelectionSnapshot selection)
    {
        int[] ids = selection.BenchChampionIds.Where(id => id > 0).Distinct().ToArray();
        // 模式尚在读取时，只要客户端提供了备战英雄就先显示，避免额外的网络等待。
        bool showBench = _isMayhem == true || _isAram || (_isMayhem == null && ids.Length > 0);
        _mayhemBench.Visible = showBench;
        if (!showBench)
        {
            _lastBench = "<unloaded>";
            ControlLifetime.ClearAndDispose(_benchChoices);
            return;
        }
        string signature = string.Join(",", ids);
        if (signature == _lastBench) return;
        _lastBench = signature;
        string title = UiLanguage.IsEnglish
            ? $"{(_isMayhem == true ? "Hex Brawl" : _isAram ? "ARAM" : "Bench")} · available champions ({ids.Length})"
            : $"{(_isMayhem == true ? "海克斯大乱斗" : _isAram ? "极地大乱斗" : "备战席")} · 可交换英雄（{ids.Length}）";
        _benchStatus.Text = string.IsNullOrEmpty(_benchMessage) ? title : $"{title}\n{_benchMessage}";
        _benchChoices.SuspendLayout();
        try
        {
            ControlLifetime.ClearAndDispose(_benchChoices);
            if (ids.Length == 0)
            {
                _benchChoices.Controls.Add(new Label
                {
                    AutoSize = true, ForeColor = Color.LightGray,
                    Text = UiLanguage.IsEnglish ? "No bench champions available" : "当前没有可交换的备战英雄"
                });
            }
            foreach (int id in ids)
            {
                string name = _champions.GetDisplayName(id);
                var button = new Button
                {
                    AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Text = string.IsNullOrWhiteSpace(name) ? id.ToString() : name,
                    Tag = id, Margin = new Padding(2), FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(53, 66, 90), ForeColor = Color.White
                };
                button.Click += async (_, _) => await SwapBenchChampionAsync(id);
                _benchChoices.Controls.Add(button);
            }
        }
        finally { _benchChoices.ResumeLayout(); }
    }

    /// <summary>请求将指定备选英雄交换为当前英雄。</summary>
    private async Task SwapBenchChampionAsync(int championId)
    {
        if (_swapping || !_tracking || IsDisposed) return;
        _swapping = true;
        int generation = _trackingGeneration;
        CancellationToken token = _trackingCancellation!.Token;
        try
        {
            ChampionSelectionSnapshot? current = await _selection.GetSessionAsync(token);
            if (!IsCurrentTracking(generation) || token.IsCancellationRequested) return;
            if (current == null || !current.BenchChampionIds.Contains(championId))
            {
                _benchMessage = UiLanguage.IsEnglish ? "Bench changed; refreshing…" : "备战席已变化，正在刷新…";
                return;
            }
            ClientFeatureResult result = await _features.SwapAramBenchAsync(championId, token);
            if (!IsCurrentTracking(generation)) return;
            _benchMessage = UiLanguage.T(result.Message);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { if (IsCurrentTracking(generation)) _benchMessage = UiLanguage.T($"换英雄失败：{ex.Message}"); }
        finally
        {
            if (IsCurrentTracking(generation))
            {
                _swapping = false;
                _lastBench = "<unloaded>";
                _lastRefresh = DateTimeOffset.MinValue;
                if (_lastSelection != null) RenderMayhemBench(_lastSelection);
                _ = RefreshAsync();
            }
        }
    }

    /// <summary>打开当前英雄的推荐方案选择界面。</summary>
    private async Task ChooseBuildAsync()
    {
        if (!_tracking || IsDisposed || !_chooseBuild.Enabled) return;
        try
        {
            _chooseBuild.Enabled = false;
            _buildStatus.Visible = true;
            _buildStatus.Text = UiLanguage.IsEnglish ? "Loading current build options…" : "正在获取当前模式的符文装备方案…";
            TopMost = false;
            string result = await Program.GameMain.coachForm.OpenOpggBuildPickerAsync(_lifetime.Token, owner: this);
            if (!IsDisposed) _buildStatus.Text = UiLanguage.T(result);
        }
        catch (Exception ex)
        {
            RuntimeDiagnostics.WriteException(ex);
            if (!IsDisposed) _buildStatus.Text = UiLanguage.T($"OP.GG 方案选择失败：{ex.Message}");
        }
        finally
        {
            if (!IsDisposed)
            {
                TopMost = true;
                _chooseBuild.Enabled = true;
            }
        }
    }

    /// <summary>加载单名队友的资料和近期表现信息。</summary>
    private async Task<string> LoadAllyAsync(ChampionSelectionMember member)
    {
        if (string.IsNullOrWhiteSpace(member.Puuid)) return UiLanguage.IsEnglish
            ? "Anonymous ally\nWaiting for client data" : "未公开身份的队友\n等待客户端提供资料";
        string name = UiLanguage.IsEnglish ? "Ally" : "队友";
        string rank = UiLanguage.IsEnglish ? "Rank unavailable" : "段位未获取";
        string recent = UiLanguage.IsEnglish ? "Recent matches unavailable" : "近期战绩未获取";
        try
        {
            var profile = await _players.GetByPuuidAsync(member.Puuid);
            name = profile?.RiotId ?? name;
        }
        catch { }
        try
        {
            RankedOverview? overview = await _ranked.GetAsync(member.Puuid);
            RankedQueue? solo = overview?.GetQueue(RankedQueues.Solo5x5);
            rank = RankedDisplayFormatter.FormatSoloQueue(solo, UiLanguage.IsEnglish);
        }
        catch { }
        try
        {
            MatchHistoryResponse? history = await _matches.GetPageAsync(member.Puuid, 0, 20);
            var games = history?.Games?.Games.Where(game => game.IsCompletedGame()).ToArray() ?? [];
            var results = games.Select(game => game.GetParticipant(member.Puuid)?.stats)
                .Where(stats => stats != null).ToArray();
            if (results.Length > 0)
            {
                double winRate = results.Count(stats => stats!.Win) * 100d / results.Length;
                double kda = results.Sum(stats => stats!.kills + stats.assists) /
                    (double)Math.Max(1, results.Sum(stats => stats!.deaths));
                recent = UiLanguage.IsEnglish
                    ? $"Last {results.Length} · Win rate {winRate:0}% · KDA {kda:0.0}"
                    : $"近 {results.Length} 场 · 胜率 {winRate:0}% · KDA {kda:0.0}";
            }
        }
        catch { }
        return $"{name}\n{rank}\n{recent}";
    }

    /// <summary>根据客户端窗口位置调整伴随窗的位置。</summary>
    private void FollowClient(Rectangle client)
    {
        Rectangle work = Screen.FromRectangle(client).WorkingArea;
        int width = Math.Min(Math.Clamp(client.Width / 4, 260, 300), work.Width);
        int height = Math.Min(Math.Clamp(client.Height * 3 / 4, 390, 490), work.Height);
        int rightSpace = work.Right - client.Right;
        int leftSpace = client.Left - work.Left;
        int x = rightSpace >= width || rightSpace >= leftSpace ? client.Right : client.Left - width;
        x = Math.Clamp(x, work.Left, work.Right - width);
        int y = Math.Clamp(client.Top, work.Top, work.Bottom - height);
        if (Bounds != new Rectangle(x, y, width, height)) Bounds = new Rectangle(x, y, width, height);
        float fontSize = Math.Clamp(width / 34f, 8f, 10f);
        if (Math.Abs(Font.Size - fontSize) > 0.25f) Font = new Font("Microsoft YaHei UI", fontSize);
        foreach (Control card in _cards.Controls) card.Width = Math.Max(200, _cards.ClientSize.Width - 20);
    }

    /// <summary>根据客户端是否可见决定伴随窗的显示状态。</summary>
    internal bool SyncClientVisibility()
    {
        if (!_tracking || IsDisposed) return false;
        Rectangle? client = _clientBounds();
        if (client == null)
        {
            if (Visible) Hide();
            return false;
        }
        FollowClient(client.Value);
        if (!Visible) Show();
        return true;
    }

    /// <summary>尝试取得客户端窗口边界，未找到时返回失败。</summary>
    private bool TryGetClientBounds(out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        // 固定跟随已找到的客户端窗口；最小化时不改找其他辅助窗口。
        if (_clientWindow != IntPtr.Zero && IsWindow(_clientWindow))
            return TryGetVisibleClientBounds(_clientWindow, out bounds);
        _clientWindow = IntPtr.Zero;
        try
        {
            foreach (Process process in Process.GetProcessesByName("LeagueClientUx"))
            {
                using (process)
                {
                    IntPtr handle = process.MainWindowHandle;
                    if (handle == IntPtr.Zero) continue;
                    if (IsIconic(handle)) { _clientWindow = handle; return false; }
                    if (!TryGetVisibleClientBounds(handle, out bounds)) continue;
                    if (bounds.Width > 500 && bounds.Height > 400)
                    {
                        _clientWindow = handle;
                        return true;
                    }
                }
            }
        }
        catch (InvalidOperationException) { /* The client exited during enumeration. */ }
        return false;
    }

    /// <summary>读取可见客户端窗口的边界，排除不可用窗口。</summary>
    internal static bool TryGetVisibleClientBounds(IntPtr handle, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (handle == IntPtr.Zero || !IsWindow(handle) || IsIconic(handle) || !IsWindowVisible(handle)) return false;
        // 切换虚拟桌面/客户端隐藏时窗口可能仍被 Win32 标记为可见。
        if (DwmGetWindowAttribute(handle, 14 /* DWMWA_CLOAKED */, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            return false;
        if (!GetWindowRect(handle, out var rect)) return false;
        var rectangle = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        if (rectangle.Width <= 0 || rectangle.Height <= 0 ||
            !Screen.AllScreens.Any(screen => screen.Bounds.IntersectsWith(rectangle))) return false;
        bounds = rectangle;
        return true;
    }

    /// <summary>与 Windows 原生矩形布局对应的坐标结构。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    /// <summary>调用 Windows API 读取窗口在屏幕上的边界。</summary>
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out Rect bounds);
    /// <summary>调用 Windows API 判断窗口是否处于最小化状态。</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);
    /// <summary>调用 Windows API 判断窗口当前是否可见。</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);
    /// <summary>调用 Windows API 验证窗口句柄是否仍然有效。</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);
    /// <summary>读取 DWM 管理的窗口属性。</summary>
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
}
