using System.Runtime.InteropServices;
using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>海克斯大乱斗专用局内侧边栏；推荐和 OCR 均只在本机处理。</summary>
internal sealed class MayhemOverlayForm : Form
{
    private readonly IAiCoachingService _context = AppCompositionRoot.AiCoachingService;
    private readonly IOpggBuildApplyService _builds = AppCompositionRoot.OpggBuildApplyService;
    private readonly IAugmentScanner _scanner = AppCompositionRoot.AugmentScanner;
    private readonly IAugmentInfoService _augments = AppCompositionRoot.AugmentInfoService;
    private readonly IGameAssetService _assets = AppCompositionRoot.GameAssetService;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 6000 };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly AntdUI.Label _status = new() { Dock = DockStyle.Top, Height = 54, ForeColor = Color.LightGray };
    private readonly AntdUI.Input _recommendations = TextPanel();
    private readonly AntdUI.Input _offered = TextPanel();
    private readonly AntdUI.Button _scan = new() { Text = "扫描当前增幅", Dock = DockStyle.Top, Height = 38 };
    private readonly TableLayoutPanel _contentLayout = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(9) };
    private readonly AntdUI.Label _titleLabel = new();
    private readonly AntdUI.Button _close = new() { Text = "×", Dock = DockStyle.Right, Width = 38 };
    private OpggBuildChoices? _choices;
    private DateTimeOffset _loadedAt;
    private int _championId;
    private bool _tracking;
    private bool _busy;
    private bool _scanning;
    private bool _collapsed;
    private string _lastOfferKey = "";
    private string _dismissedOfferKey = "";
    private string _pendingOfferKey = "";
    private int _pendingOfferScans;
    private bool _sawOfferGap;
    private DateTimeOffset _lastAutoScanAt;
    private int _autoScanMisses;
    private bool _excludedFromCapture;
    private bool _titleMousePressed;
    private bool _titleDragging;
    private Point _titleMouseDownScreen;

    public MayhemOverlayForm()
    {
        Text = "海克斯增幅推荐";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        Size = new Size(420, 650);
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(22, 27, 39);
        ForeColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 10);
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(screen.Right - Width - 18, screen.Top + (screen.Height - Height) / 2);

        var title = new AntdUI.Panel { Dock = DockStyle.Top, Height = 46, BackColor = Color.FromArgb(35, 43, 62), Radius = 0 };
        _titleLabel.Dock = DockStyle.Fill;
        _titleLabel.ForeColor = Color.White;
        _titleLabel.Font = new Font(Font, FontStyle.Bold);
        _titleLabel.TextAlign = ContentAlignment.MiddleLeft;
        _titleLabel.Padding = new Padding(12, 0, 0, 0);
        UpdateTitle();
        title.Controls.Add(_titleLabel);
        _close.Click += (_, _) => Collapse();
        title.Controls.Add(_close);
        foreach (Control control in new Control[] { title, _titleLabel })
        {
            control.MouseDown += (_, e) => OnTitleMouseDown(e);
            control.MouseMove += (_, e) => OnTitleMouseMove(e);
            control.MouseUp += (_, e) => OnTitleMouseUp(e);
        }

        _contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 61));
        _contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 39));
        _contentLayout.Controls.Add(_recommendations, 0, 0);
        _contentLayout.Controls.Add(_offered, 0, 1);
        Controls.Add(_contentLayout);
        Controls.Add(_scan);
        Controls.Add(_status);
        Controls.Add(title);
        _scan.Click += async (_, _) => await ScanAsync();
        _timer.Tick += async (_, _) => await PollAsync();
        UiLanguage.Changed += LanguageChanged;
        Disposed += (_, _) =>
        {
            _lifetime.Cancel();
            UiLanguage.Changed -= LanguageChanged;
            _timer.Dispose();
            _lifetime.Dispose();
        };
    }

    private async void LanguageChanged(object? sender, EventArgs args)
    {
        if (IsDisposed) return;
        UpdateTitle();
        _offered.Clear();
        if (_choices != null)
            try { await RenderRecommendationsAsync(); }
            catch (Exception ex) { if (!IsDisposed) _status.Text = UiLanguage.T($"数据暂不可用：{ex.Message}"); }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // The sidebar overlaps the right offer on smaller screens. Keep it visible
        // to the player without including it in CopyFromScreen screenshots.
        _excludedFromCapture = SetWindowDisplayAffinity(Handle, 0x11); // WDA_EXCLUDEFROMCAPTURE
    }

    public void StartTracking()
    {
        if (IsDisposed) return;
        if (_tracking) return;
        _tracking = true;
        _status.Text = UiLanguage.T("正在读取当前对局与海克斯数据…");
        if (!Visible) Show();
        _timer.Start();
        _ = PollAsync();
    }

    public void StopTracking()
    {
        if (IsDisposed) return;
        _tracking = false;
        _timer.Stop();
        if (_collapsed) Expand();
        _lastOfferKey = _dismissedOfferKey = _pendingOfferKey = "";
        _pendingOfferScans = 0;
        _sawOfferGap = false;
        Hide();
    }

    private void Collapse()
    {
        if (_collapsed || IsDisposed) return;
        _collapsed = true;
        _dismissedOfferKey = _lastOfferKey;
        _pendingOfferKey = "";
        _pendingOfferScans = 0;
        _sawOfferGap = false;
        _autoScanMisses = 0;
        int right = Right;
        _contentLayout.Visible = _scan.Visible = _status.Visible = _close.Visible = false;
        UpdateTitle();
        Size = new Size(210, 46);
        Left = right - Width;
    }

    private void Expand()
    {
        if (!_collapsed || IsDisposed) return;
        _collapsed = false;
        int right = Right;
        Size = new Size(420, 650);
        Left = right - Width;
        UpdateTitle();
        _contentLayout.Visible = _scan.Visible = _status.Visible = _close.Visible = true;
    }

    private void UpdateTitle() => _titleLabel.Text = _collapsed
        ? (UiLanguage.IsEnglish ? "Mayhem offers · click to open" : "海克斯推荐 · 点击展开")
        : (UiLanguage.IsEnglish ? "Mayhem · augment sidebar" : "海克斯大乱斗 · 增幅侧边栏");

    private void OnTitleMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _titleMousePressed = true;
        _titleDragging = false;
        _titleMouseDownScreen = Cursor.Position;
    }

    private void OnTitleMouseMove(MouseEventArgs e)
    {
        if (!_titleMousePressed || _titleDragging || e.Button != MouseButtons.Left) return;
        Point current = Cursor.Position;
        Size threshold = SystemInformation.DragSize;
        if (Math.Abs(current.X - _titleMouseDownScreen.X) < threshold.Width / 2 &&
            Math.Abs(current.Y - _titleMouseDownScreen.Y) < threshold.Height / 2) return;
        _titleDragging = true;
        Drag(e);
    }

    private void OnTitleMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !_titleMousePressed) return;
        _titleMousePressed = false;
        if (_titleDragging) return;
        if (_collapsed) Expand();
        else Collapse();
    }

    private async Task PollAsync()
    {
        if (!_tracking || _busy || IsDisposed) return;
        _busy = true;
        try
        {
            var context = await _context.CollectContextAsync(_lifetime.Token);
            if (!_tracking || IsDisposed || _lifetime.IsCancellationRequested) return;
            bool mayhem = context.QueueId is 2400 or 3270 ||
                context.GameMode.StartsWith("KIWI", StringComparison.OrdinalIgnoreCase);
            if (context.Phase != "InProgress" || !mayhem)
            {
                if (Visible) Hide();
                return;
            }
            if (!Visible) Show();
            if (DateTimeOffset.UtcNow - _lastAutoScanAt >= TimeSpan.FromSeconds(_collapsed ? 6 : 12))
            {
                _lastAutoScanAt = DateTimeOffset.UtcNow;
                await ScanOffersAsync(automatic: true);
            }
            if (context.MyChampionId <= 0)
            { _status.Text = UiLanguage.T("正在等待当前英雄数据…"); return; }
            if (_choices == null || _championId != context.MyChampionId ||
                DateTimeOffset.UtcNow - _loadedAt > TimeSpan.FromMinutes(20))
            {
                _status.Text = UiLanguage.T("正在获取本版本海克斯出装与增幅数据…");
                _choices = await _builds.GetBuildChoicesAsync(context.MyChampionId, context.MyRole,
                    new OpggBuildRequest(context.GameMode, context.QueueId), _lifetime.Token);
                if (!_tracking || IsDisposed || _lifetime.IsCancellationRequested) return;
                _championId = context.MyChampionId;
                _loadedAt = DateTimeOffset.UtcNow;
                await RenderRecommendationsAsync();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            RuntimeDiagnostics.Report("增幅推荐", "失败", $"{ex.GetType().Name}: {ex.Message}");
            if (!IsDisposed)
                _status.Text = UiLanguage.T($"数据暂不可用：{ex.Message}") +
                    (UiLanguage.IsEnglish ? "; OCR scanning is still available" : "；仍可扫描增幅卡片");
        }
        finally { _busy = false; }
    }

    private async Task RenderRecommendationsAsync()
    {
        if (_choices is not { Succeeded: true })
        {
            _status.Text = UiLanguage.T(_choices?.Message ?? "专属数据暂不可用") +
                (UiLanguage.IsEnglish ? "; OCR scanning is still available" : "；仍可扫描增幅卡片");
            return;
        }
        _status.Text = UiLanguage.IsEnglish
            ? $"{_choices.ChampionName} · Mayhem augments and build · scan the offers in game"
            : $"{_choices.ChampionName} · 专属增幅与出装；游戏内可点击扫描";
        var top = (_choices.Augments ?? Array.Empty<OpggAugmentRecommendation>()).Take(12).ToArray();
        var named = (await _augments.ResolveAsync(top.Select(item => item.Id))).ToDictionary(item => item.Id);
        if (IsDisposed || _lifetime.IsCancellationRequested) return;
        var lines = new List<string> { UiLanguage.IsEnglish ? "Recommended augments · champion win rate" : "推荐增幅（英雄样本胜率）" };
        foreach (var augment in top)
            lines.Add($"{DisplayName(named.GetValueOrDefault(augment.Id))}  {augment.WinRate:0.0}%  · {augment.Matches:N0} " +
                (UiLanguage.IsEnglish ? "matches" : "场"));
        var core = _choices.Options.FirstOrDefault()?.CoreItemIds ?? Array.Empty<int>();
        if (core.Count > 0)
        {
            var itemNames = await Task.WhenAll(core.Select(async id => await _assets.GetItemNameAsync(id) ?? $"#{id}"));
            if (IsDisposed || _lifetime.IsCancellationRequested) return;
            lines.Add("");
            lines.Add((UiLanguage.IsEnglish ? "Core items: " : "专属核心出装：") + string.Join(" → ", itemNames));
        }
        _recommendations.Text = string.Join(Environment.NewLine, lines);
    }

    private async Task ScanAsync()
    {
        if (_scanning) return;
        _scan.Enabled = false;
        try
        {
            _offered.Text = UiLanguage.T("正在识别屏幕中央的增幅卡片…");
            await ScanOffersAsync(automatic: false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { if (!IsDisposed) _offered.Text = UiLanguage.T($"OCR 扫描失败：{ex.Message}"); }
        finally { if (!IsDisposed) _scan.Enabled = true; }
    }

    private async Task ScanOffersAsync(bool automatic)
    {
        if (_scanning) return;
        _scanning = true;
        Point? originalLocation = null;
        try
        {
            if (!_excludedFromCapture && Visible)
            {
                // Older Windows builds do not support WDA_EXCLUDEFROMCAPTURE.
                // Move the sidebar outside the virtual desktop for this capture.
                originalLocation = Location;
                Location = new Point(SystemInformation.VirtualScreen.Right + 32, Top);
                await Task.Delay(80, _lifetime.Token);
            }
            AugmentScanResult result = await _scanner.ScanAsync(_lifetime.Token);
            if (IsDisposed || _lifetime.IsCancellationRequested || !_tracking) return;
            if (result.AugmentIds.Count == 0)
            {
                _pendingOfferKey = "";
                _pendingOfferScans = 0;
                RuntimeDiagnostics.Report("增幅 OCR", "未识别", result.Message);
                if (!automatic) _offered.Text = UiLanguage.T(result.Message);
                else
                {
                    if (++_autoScanMisses >= 2) _offered.Clear();
                    if (_collapsed && _autoScanMisses >= 3) _sawOfferGap = true;
                }
                return;
            }
            _autoScanMisses = 0;
            RuntimeDiagnostics.Report("增幅 OCR", "已识别", result.Message);
            string offerKey = string.Join(",", result.AugmentIds.Order());
            if (_collapsed && automatic && _sawOfferGap && result.AugmentIds.Count >= 2 &&
                offerKey != _dismissedOfferKey)
            {
                _pendingOfferScans = offerKey == _pendingOfferKey ? _pendingOfferScans + 1 : 1;
                _pendingOfferKey = offerKey;
                if (_pendingOfferScans >= 2) Expand();
            }
            else
            {
                _pendingOfferKey = "";
                _pendingOfferScans = 0;
            }
            _lastOfferKey = offerKey;
            var named = (await _augments.ResolveAsync(result.AugmentIds)).ToDictionary(item => item.Id);
            if (IsDisposed || _lifetime.IsCancellationRequested || !_tracking) return;
            var stats = (_choices?.Augments ?? Array.Empty<OpggAugmentRecommendation>())
                .ToDictionary(item => item.Id);
            var scored = result.AugmentIds.Select(id => (Id: id, Stats: stats.GetValueOrDefault(id)))
                .OrderByDescending(item => item.Stats?.WinRate ?? -1).ToArray();
            _offered.Text = (UiLanguage.IsEnglish ? "Detected offers · recommended order" : "本次识别 · 建议顺序") + Environment.NewLine +
                string.Join(Environment.NewLine, scored.Select((item, index) =>
                    $"{index + 1}. {DisplayName(named.GetValueOrDefault(item.Id))}  " +
                    (item.Stats == null ? (UiLanguage.IsEnglish ? "No champion sample" : "暂无该英雄样本") :
                        $"{item.Stats.WinRate:0.0}% · {item.Stats.Matches:N0} " + (UiLanguage.IsEnglish ? "matches" : "场"))));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Exception cause = ex.GetBaseException();
            RuntimeDiagnostics.Report("增幅 OCR", "失败", $"{cause.GetType().Name}: {cause.Message}");
            if (!IsDisposed && !automatic) _offered.Text = UiLanguage.T($"OCR 扫描失败：{cause.Message}");
        }
        finally
        {
            if (originalLocation is Point location && !IsDisposed) Location = location;
            _scanning = false;
        }
    }

    private static AntdUI.Input TextPanel() => new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, AutoScroll = true,
        BackColor = Color.FromArgb(22, 27, 39), ForeColor = Color.White,
        Font = new Font("Microsoft YaHei UI", 10.5f)
    };

    private static string DisplayName(AugmentInfo? item) => item == null ? "未知增幅" :
        UiLanguage.IsEnglish && !string.IsNullOrWhiteSpace(item.EnglishName) ? item.EnglishName : item.Name;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

    private void Drag(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
