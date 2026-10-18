using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>Only owns data/timers; the C++ renderer owns every visible game panel.</summary>
internal sealed class MayhemOverlayController : IDisposable
{
    internal Func<Rectangle?> GameBounds = () => WindowHoldController.TryGetForegroundGameBounds(out var bounds) ? bounds : null;
    private readonly IAiCoachingService _context = AppCompositionRoot.AiCoachingService;
    private readonly IOpggBuildApplyService _builds = AppCompositionRoot.OpggBuildApplyService;
    private readonly IAugmentScanner _scanner;
    private readonly IGameOverlayBridge _overlay;
    private readonly IAugmentInfoService _augments = AppCompositionRoot.AugmentInfoService;
    private readonly IGameAssetService _assets = AppCompositionRoot.GameAssetService;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 6000 };
    private readonly System.Windows.Forms.Timer _visibilityTimer = new() { Interval = 100 };
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyDictionary<int, AugmentInfo> _namedAugments = new Dictionary<int, AugmentInfo>();
    private string _coreRecommendation = "", _status = "", _recommendations = "", _offered = "";
    private int _rarity;
    private OpggBuildChoices? _choices;
    private DateTimeOffset _loadedAt, _lastAutoScanAt;
    private int _championId, _pendingOfferScans, _autoScanMisses;
    private bool _tracking, _mayhemActive, _busy, _scanning, _collapsed, _sawOfferGap;
    private string _lastOfferKey = "", _dismissedOfferKey = "", _pendingOfferKey = "";
    public bool IsDisposed { get; private set; }
    public MayhemOverlayController() : this(AppCompositionRoot.AugmentScanner) { }
    internal MayhemOverlayController(IAugmentScanner scanner, IGameOverlayBridge? overlay = null)
    {
        _scanner=scanner; _overlay=overlay ?? AppCompositionRoot.GameOverlay;
        _timer.Tick += async (_,_)=>await PollAsync();
        _visibilityTimer.Tick += (_,_)=>MaintainOverlayVisibility();
        UiLanguage.Changed += LanguageChanged;
    }
    private async void LanguageChanged(object? sender, EventArgs args)
    {
        if(IsDisposed) return;
        _offered="";
        if(_choices!=null) try { await RenderRecommendationsAsync(); }
        catch(Exception ex) { if(!IsDisposed) _status=UiLanguage.T($"数据暂不可用：{ex.Message}"); }
    }
    public void StartTracking()
    {
        if(IsDisposed || _tracking) return;
        _tracking=true; _status=UiLanguage.T("正在读取当前对局与海克斯数据…");
        _timer.Start(); _visibilityTimer.Start(); _=PollAsync();
    }
    public void StopTracking()
    {
        if(IsDisposed) return;
        _tracking=_mayhemActive=false; _timer.Stop(); _visibilityTimer.Stop(); _overlay.Disable();
        _collapsed=false; _lastOfferKey=_dismissedOfferKey=_pendingOfferKey="";
        _pendingOfferScans=0; _sawOfferGap=false;
    }
    internal void MaintainOverlayVisibility()
    {
        if(IsDisposed) return;
        if(!_tracking || !_mayhemActive) { _overlay.Disable(); return; }
        int command=_overlay.Publish(new(UiLanguage.T("海克斯大乱斗 · 增幅侧边栏"),_status,_recommendations,_offered,_rarity,_collapsed,_scanning));
        if(command==1 && !_scanning) _=ScanAsync();
        else if(command==2) { if(_collapsed) Expand(); else Collapse(); }
        else if(command is >=10 and <=12) { _rarity=command-10; RenderSelectedRarity(); }
    }
    private void Collapse()
    {
        _collapsed=true; _dismissedOfferKey=_lastOfferKey; _pendingOfferKey="";
        _pendingOfferScans=0; _sawOfferGap=false; _autoScanMisses=0;
    }
    private void Expand() => _collapsed=false;
    /// <summary>定期读取游戏上下文和推荐数据。</summary>
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
            _mayhemActive = context.Phase == "InProgress" && mayhem;
            MaintainOverlayVisibility();
            if (!_mayhemActive)
            {
                return;
            }
            if (GameBounds() != null && DateTimeOffset.UtcNow - _lastAutoScanAt >= TimeSpan.FromSeconds(_collapsed || _autoScanMisses >= 2 ? 60 : 12))
            {
                _lastAutoScanAt = DateTimeOffset.UtcNow;
                await ScanOffersAsync(automatic: true);
            }
            if (context.MyChampionId <= 0)
            { _status = UiLanguage.T("正在等待当前英雄数据…"); return; }
            if (_choices == null || _championId != context.MyChampionId ||
                DateTimeOffset.UtcNow - _loadedAt > TimeSpan.FromMinutes(20))
            {
                _status = UiLanguage.T("正在获取本版本海克斯出装与增幅数据…");
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
                _status = UiLanguage.T($"数据暂不可用：{ex.Message}") +
                    (UiLanguage.IsEnglish ? "; OCR scanning is still available" : "；仍可扫描增幅卡片");
        }
        finally { _busy = false; }
    }

    /// <summary>将当前英雄的增幅推荐转换为展示内容。</summary>
    private async Task RenderRecommendationsAsync()
    {
        if (_choices is not { Succeeded: true })
        {
            _status = UiLanguage.T(_choices?.Message ?? "专属数据暂不可用") +
                (UiLanguage.IsEnglish ? "; OCR scanning is still available" : "；仍可扫描增幅卡片");
            return;
        }
        _status = UiLanguage.IsEnglish
            ? $"{_choices.ChampionName} · Mayhem augments and build · scan the offers in game"
            : $"{_choices.ChampionName} · 专属增幅与出装；游戏内可点击扫描";
        var choices = _choices;
        var all = choices.Augments ?? Array.Empty<OpggAugmentRecommendation>();
        var named = (await _augments.ResolveAsync(all.Select(item => item.Id))).ToDictionary(item => item.Id);
        if (IsDisposed || _lifetime.IsCancellationRequested) return;
        string coreRecommendation = "";
        var core = choices.Options.FirstOrDefault()?.CoreItemIds ?? Array.Empty<int>();
        if (core.Count > 0)
        {
            var itemNames = await Task.WhenAll(core.Select(async id => await _assets.GetItemNameAsync(id) ?? $"#{id}"));
            if (IsDisposed || _lifetime.IsCancellationRequested) return;
            coreRecommendation = (UiLanguage.IsEnglish ? "Core items: " : "专属核心出装：") + string.Join(" → ", itemNames);
        }
        if (!ReferenceEquals(choices, _choices)) return;
        _namedAugments = named;
        _coreRecommendation = coreRecommendation;
        RenderSelectedRarity();
    }

    /// <summary>取得指定稀有度的显示名称。</summary>
    private static string RarityName(int rarity) => rarity switch
    {
        0 => UiLanguage.IsEnglish ? "Silver" : "白银",
        1 => UiLanguage.IsEnglish ? "Gold" : "黄金",
        2 => UiLanguage.IsEnglish ? "Prismatic" : "棱彩",
        _ => UiLanguage.IsEnglish ? "Unknown type" : "类型未知"
    };

    /// <summary>展示当前选中稀有度下的强化推荐。</summary>
    private void RenderSelectedRarity()
    {
        if (IsDisposed || _choices is not { Succeeded: true }) return;
        int rarity = Math.Clamp(_rarity, 0, 2);
        var top = AugmentRecommendationGroups.Select(_choices.Augments ?? [], _namedAugments, rarity);
        var lines = new List<string>
        {
            UiLanguage.IsEnglish ? $"{RarityName(rarity)} augments · matches ↓, win rate ↓"
                : $"{RarityName(rarity)}增幅推荐（场数↓，同场数按胜率↓）"
        };
        foreach (var augment in top)
            lines.Add($"{DisplayName(_namedAugments.GetValueOrDefault(augment.Id))}  {augment.WinRate:0.0}%  · {augment.Matches:N0} " +
                (UiLanguage.IsEnglish ? "matches" : "场"));
        if (top.Count == 0)
            lines.Add(UiLanguage.IsEnglish ? "No champion samples for this type yet" : "暂无该类型的英雄推荐样本");
        if (!string.IsNullOrEmpty(_coreRecommendation))
        {
            lines.Add("");
            lines.Add(_coreRecommendation);
        }
        _recommendations = string.Join(Environment.NewLine, lines);
    }

    /// <summary>执行本机强化选项识别。</summary>
    private async Task ScanAsync()
    {
        if (_scanning || GameBounds() == null) return;
        try
        {
            _offered = UiLanguage.T("正在识别屏幕中央的增幅卡片…");
            await ScanOffersAsync(automatic: false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { if (!IsDisposed) _offered = UiLanguage.T($"OCR 扫描失败：{ex.Message}"); }
    }

    /// <summary>识别当前屏幕中的强化选项并更新推荐结果。</summary>
    private async Task ScanOffersAsync(bool automatic)
    {
        if (_scanning || GameBounds() == null) return;
        _scanning = true;
        try
        {
            // Capture only the central card region; keep the sidebar's position and
            // display affinity stable to avoid a visible flash on each OCR pass.
            AugmentScanResult result = await _scanner.ScanAsync(_lifetime.Token);
            if (IsDisposed || _lifetime.IsCancellationRequested || !_tracking) return;
            if (result.AugmentIds.Count == 0)
            {
                _pendingOfferKey = "";
                _pendingOfferScans = 0;
                RuntimeDiagnostics.Report("增幅 OCR", "未识别", result.Message);
                if (!automatic) _offered = UiLanguage.T(result.Message);
                else
                {
                    if (++_autoScanMisses >= 2) _offered = "";
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
                .OrderByDescending(item => item.Stats?.Matches ?? -1)
                .ThenByDescending(item => item.Stats?.WinRate ?? -1).ToArray();
            _offered = (UiLanguage.IsEnglish ? "Detected offers · recommended order" : "本次识别 · 建议顺序") + Environment.NewLine +
                string.Join(Environment.NewLine, scored.Select((item, index) =>
                    $"{index + 1}. [{RarityName(named.GetValueOrDefault(item.Id)?.Rarity ?? item.Stats?.Rarity ?? -1)}] " +
                    $"{DisplayName(named.GetValueOrDefault(item.Id))}  " +
                    (item.Stats == null ? (UiLanguage.IsEnglish ? "No champion sample" : "暂无该英雄样本") :
                        $"{item.Stats.WinRate:0.0}% · {item.Stats.Matches:N0} " + (UiLanguage.IsEnglish ? "matches" : "场"))));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Exception cause = ex.GetBaseException();
            RuntimeDiagnostics.Report("增幅 OCR", "失败", $"{cause.GetType().Name}: {cause.Message}");
            if (!IsDisposed && !automatic) _offered = UiLanguage.T($"OCR 扫描失败：{cause.Message}");
        }
        finally
        {
            _scanning = false;
        }
    }

    private static string DisplayName(AugmentInfo? item) => item == null ? "未知增幅" :
        UiLanguage.IsEnglish && !string.IsNullOrWhiteSpace(item.EnglishName) ? item.EnglishName : item.Name;
    public void Dispose()
    {
        if(IsDisposed) return;
        StopTracking(); IsDisposed=true; _lifetime.Cancel(); UiLanguage.Changed-=LanguageChanged;
        _timer.Dispose(); _visibilityTimer.Dispose(); _lifetime.Dispose();
    }
}
