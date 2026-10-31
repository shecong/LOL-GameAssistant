using ToolTip = LOL_GameAssistant.Helper.UiToolTip;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>仅下载静态图标，不请求任何单局详情；图片归当前控件所有。</summary>
internal sealed class MatchRowImages(IGameAssetService assets, Action changed) : IDisposable
{
    private static readonly SemaphoreSlim Gate = new(8);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<(string Kind, int Id), Image> _images = [];
    private readonly HashSet<(string Kind, int Id)> _requested = [];
    private bool _disposed;
    internal Image? Get(string kind, int id) => _images.GetValueOrDefault((kind, id));
    internal async Task LoadAsync(string kind, int id)
    {
        if (_disposed || id <= 0 || !_requested.Add((kind, id))) return;
        bool entered = false;
        try
        {
            await Gate.WaitAsync(_lifetime.Token); entered = true;
            GameAsset? asset = kind switch
            {
                "champion" => await assets.GetChampionIconAsync(id, _lifetime.Token),
                "spell" => await assets.GetSummonerSpellIconAsync(id, _lifetime.Token),
                "rune" => await assets.GetRuneIconAsync(id, _lifetime.Token),
                _ => await assets.GetItemIconAsync(id, _lifetime.Token)
            };
            if (_disposed || asset == null || asset.IsEmpty) return;
            using var stream = new MemoryStream(asset.Content);
            using var original = Image.FromStream(stream);
            _images[(kind, id)] = new Bitmap(original);
            changed();
        }
        catch (Exception error) when (error is OperationCanceledException or HttpRequestException or IOException or ArgumentException or InvalidOperationException) { }
        finally { if (entered) Gate.Release(); }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _lifetime.Cancel();
        foreach (var image in _images.Values) image.Dispose();
        _images.Clear(); _lifetime.Dispose();
    }
}

internal readonly record struct MatchListColumn(string Title, Rectangle Bounds);

internal static class MatchListColumns
{
    internal static MatchListColumn[] Get(int width, int height, int dpi)
    {
        int S(int n) => n * dpi / 96;
        bool wide = width >= S(1120), medium = width >= S(680), small = width < S(480);
        int x = S(14), end = Math.Max(x, width - S(26));
        int result = S(small ? 46 : 70), kda = S(small ? 95 : 120), duration = S(65), time = S(112);
        int mode = medium ? S(145) : 0, items = wide ? S(207) : 0;
        int hero = Math.Max(S(70), end - x - result - kda - (small ? 0 : duration + time) - mode - items);
        if (wide && hero > S(300))
        {
            int spare = hero - S(300); hero = S(300);
            mode += spare / 4; kda += spare / 4; items += spare / 3; time += spare - spare / 4 * 2 - spare / 3;
        }
        var columns = new List<MatchListColumn>();
        void Add(string title, int size) { if (size > 0) { columns.Add(new(title, new Rectangle(x, 0, size, height))); x += size; } }
        Add("结果", result); Add("英雄", hero); Add("模式", mode); Add("K / D / A", kda);
        Add("出装", items); Add("时长", small ? 0 : duration); Add("时间", small ? 0 : time);
        return columns.ToArray();
    }
    internal static void Text(Graphics graphics, string text, Font font, Color color, Rectangle area, TextFormatFlags extra = 0) =>
        TextRenderer.DrawText(graphics, text, font, area, color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | extra);
}

internal sealed class MatchHistoryListHeader : Control, IThemeAware
{
    private readonly Func<int>? _contentWidth;
    internal MatchHistoryListHeader(Func<int>? contentWidth = null) { _contentWidth = contentWidth; DoubleBuffered = true; Height = 32; Font = new Font(UiMetrics.FontFamily, 9.5F); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.TranslateTransform(4, 0);
        foreach (var column in MatchListColumns.Get(Math.Max(0, Math.Min(Width, _contentWidth?.Invoke() ?? Width) - 8), Height, DeviceDpi))
            MatchListColumns.Text(e.Graphics, UiLanguage.T(column.Title), Font, UiTheme.Palette.TextSecondary, column.Bounds);
    }
    public void ApplyTheme(ThemePalette palette) { BackColor = palette.SurfaceRaised; Invalidate(); }
}

/// <summary>固定高度的摘要行。选中只是 UI 状态，网络加载由查询页统一管理。</summary>
internal sealed class MatchHistoryListRow : UserControl, IThemeAware
{
    private readonly MatchParticipant? _mine;
    private MatchHonors _honors = new(null, [], false);
    private readonly string _champion;
    private readonly RankedLpStore _lpStore;
    private readonly string _puuid;
    private readonly MatchRowImages _images;
    private readonly ToolTip _tip = new();
    private bool _hover;
    private bool _selected;
    internal MatchHistoryGame Game { get; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool Selected { get => _selected; set { _selected = value; Invalidate(); } }
    internal event Action<MatchHistoryGame>? Activated;

    internal MatchHistoryListRow(MatchHistoryGame game, string puuid, IGameAssetService assets, IChampionCatalog champions, RankedLpStore? lpStore = null)
    {
        Game = game; _mine = game.GetParticipant(puuid);
        _lpStore = lpStore ?? RankedLpStore.Shared; _puuid = puuid;
        _lpStore.Changed += OnLpChanged;
        _champion = _mine == null ? "英雄未提供" : champions.GetDisplayName(_mine.championId);
        if (string.IsNullOrWhiteSpace(_champion)) _champion = _mine == null ? "英雄未提供" : $"英雄 {_mine.championId}";
        Font = new Font(UiMetrics.FontFamily, 10F);
        _images = new(assets, () => { if (!IsDisposed) Invalidate(); });
        SetStyle(ControlStyles.Selectable | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        Cursor = Cursors.Hand; TabStop = true;
        Margin = Padding.Empty;
        Height = 50; MinimumSize = new Size(260, 0);
        SetHonors(MatchHonorCalculator.ForPlayer(game, _mine));
        ApplyTheme(UiTheme.Palette);
    }
    private string ResultText => !Game.IsCompletedGame() ? "重开" : _mine?.stats is not { HasWinResult: true } ? "未知" : _mine.IsWin() ? "胜利" : "失败";
    private string KdaText => _mine?.stats is { HasKdaResult: true } ? _mine.GetKdaText() : "—";
    internal string? LpText => Game.QueueId is not (420 or 440) ? null : _lpStore.Get(Game.PlatformId, _puuid, Game.GameId) is int delta ? $"{delta:+0;-0;0} LP" : "胜点 —";
    private void OnLpChanged()
    {
        if (IsDisposed || Disposing) return;
        if (!IsHandleCreated) return;
        try { BeginInvoke(new Action(() => { if (!IsDisposed) SetHonors(_honors); })); }
        catch (InvalidOperationException) { }
    }
    internal MatchHonors Honors => _honors;
    internal void ApplyDetailHonors(MatchDetail detail, string puuid)
    {
        if (detail.GameId == Game.GameId && detail.GetParticipant(puuid) is { } player)
            SetHonors(MatchHonorCalculator.ForPlayer(detail, player));
    }
    private void SetHonors(MatchHonors honors)
    {
        _honors = honors;
        AccessibleName = $"{ResultText} · {_champion} · {Game.GetModeText()} · {KdaText} · {honors.Text}" + (LpText == null ? "" : " · " + LpText);
        _tip.SetToolTip(this, $"{AccessibleName}\n{CreatedText}\n{MatchHonorCalculator.Explanation}" +
            (honors.MvpEvaluated ? "" : "\nMVP 暂无法评选，需完整双方数据与明确胜负。") +
            (LpText == "胜点 —" ? "\n该场没有可核对的赛前赛后快照；结算同步后会自动更新，未采集的历史对局无法反推。"
                : LpText != null ? "\n胜负场次已核对仅增加该局；数值为赛前赛后段位积分差（跨小段按100LP折算）。升降段重置、惩罚或补偿也可能影响该差值，不代表服务器单独发放的对局奖励。" : "") + "\n点击加载本局详情，列表位置不变。");
        Invalidate();
    }
    private string CreatedText
    {
        get { try { return Game.GameCreation > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(Game.GameCreation).ToLocalTime().ToString("MM-dd HH:mm") : "时间未知"; }
            catch (ArgumentOutOfRangeException) { return "时间未知"; } }
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SetHonors(_honors);
        if (IsDisposed || _mine == null) return;
        _ = _images.LoadAsync("champion", _mine.championId);
        _ = _images.LoadAsync("spell", _mine.Spell1Id); _ = _images.LoadAsync("spell", _mine.Spell2Id);
        if (_mine.stats is { } s)
            foreach (int item in new[] { s.item0, s.item1, s.item2, s.item3, s.item4, s.item5, s.item6 }) _ = _images.LoadAsync("item", item);
    }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); Height = 50 * DeviceDpi / 96; }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnClick(EventArgs e) { base.OnClick(e); Focus(); Activated?.Invoke(Game); }
    protected override void OnKeyDown(KeyEventArgs e)
    { if (e.KeyCode is Keys.Enter or Keys.Space) { e.Handled = true; e.SuppressKeyPress = true; Activated?.Invoke(Game); } base.OnKeyDown(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var p = UiTheme.Palette; int S(int n) => n * DeviceDpi / 96;
        using var background = new SolidBrush(_selected ? (p.IsDark ? Color.FromArgb(22, 49, 80) : Color.FromArgb(224, 238, 255)) : _hover ? p.SurfaceRaised : p.Surface);
        e.Graphics.FillRectangle(background, ClientRectangle);
        Color resultColor = ResultText == "胜利" ? Color.FromArgb(40, 182, 143) : ResultText == "失败" ? Color.FromArgb(229, 99, 116) : p.TextSecondary;
        using var stripe = new SolidBrush(_selected ? p.Accent : resultColor); e.Graphics.FillRectangle(stripe, 0, S(7), S(3), Height - S(14));
        using var line = new Pen(p.Border); e.Graphics.DrawLine(line, S(10), Height - 1, Width, Height - 1);
        using var bold = new Font(Font, FontStyle.Bold);
        foreach (var column in MatchListColumns.Get(Width, Height, DeviceDpi))
        {
            var rect = column.Bounds;
            switch (column.Title)
            {
                case "结果":
                    MatchListColumns.Text(e.Graphics, UiLanguage.T(ResultText), bold, resultColor, LpText == null ? rect : new(rect.X, S(2), rect.Width, S(24)));
                    if (LpText != null)
                    {
                        using var lpFont = new Font(UiMetrics.FontFamily, 7.5F);
                        MatchListColumns.Text(e.Graphics, LpText, lpFont, _lpStore.Get(Game.PlatformId, _puuid, Game.GameId).HasValue ? resultColor : p.TextSecondary, new(rect.X, S(26), rect.Width, S(20)));
                    }
                    break;
                case "英雄":
                    int icon = S(32), y = (Height - icon) / 2;
                    DrawIcon(e.Graphics, "champion", _mine?.championId ?? 0, new(rect.X, y, icon, icon));
                    bool spells = rect.Width >= S(190);
                    int heroWidth = Math.Max(0, rect.Width - icon - S(spells ? 38 : 14));
                    MatchListColumns.Text(e.Graphics, _champion, bold, p.TextPrimary,
                        new(rect.X + icon + S(8), _honors.Feats.Count > 0 ? S(2) : 0, heroWidth, _honors.Feats.Count > 0 ? S(24) : Height));
                    if (_honors.Feats.Count > 0)
                    {
                        using var featFont = new Font(UiMetrics.FontFamily, 7.5F, FontStyle.Bold);
                        MatchHonorBadges.Draw(e.Graphics, new(null, _honors.Feats, _honors.MvpEvaluated), featFont,
                            new(rect.X + icon + S(8), S(28), heroWidth, S(19)));
                    }
                    if (spells)
                    {
                        DrawIcon(e.Graphics, "spell", _mine?.Spell1Id ?? 0, new(rect.Right - S(25), S(7), S(16), S(16)));
                        DrawIcon(e.Graphics, "spell", _mine?.Spell2Id ?? 0, new(rect.Right - S(25), S(26), S(16), S(16)));
                    }
                    break;
                case "模式": MatchListColumns.Text(e.Graphics, UiLanguage.T(Game.GetModeText()), Font, p.TextSecondary, rect); break;
                case "K / D / A":
                    MatchListColumns.Text(e.Graphics, KdaText.Replace("/", " / "), bold, p.TextPrimary,
                        _honors.Mvp is null ? rect : new(rect.X, S(2), rect.Width, S(24)));
                    if (_honors.Mvp is not null)
                    {
                        using var badgeFont = new Font(UiMetrics.FontFamily, 7.5F, FontStyle.Bold);
                        var kdaHonors = new MatchHonors(_honors.Mvp, [], true);
                        MatchHonorBadges.Draw(e.Graphics, kdaHonors, badgeFont, new(rect.X, S(28), rect.Width - S(4), S(19)));
                    }
                    break;
                case "时长": MatchListColumns.Text(e.Graphics, Game.GameDuration > 0 ? $"{Game.GameDuration / 60}:{Game.GameDuration % 60:D2}" : "—", Font, p.TextSecondary, rect); break;
                case "时间": MatchListColumns.Text(e.Graphics, CreatedText, Font, p.TextSecondary, rect); break;
                case "出装":
                    if (_mine?.stats is { } stats)
                    {
                        int[] ids = [stats.item0, stats.item1, stats.item2, stats.item3, stats.item4, stats.item5, stats.item6];
                        for (int i = 0; i < ids.Length; i++) DrawIcon(e.Graphics, "item", ids[i], new(rect.X + i * S(27), (Height - S(23)) / 2, S(23), S(23)));
                    }
                    break;
            }
        }
        MatchListColumns.Text(e.Graphics, "›", Font, _selected ? p.Accent : p.TextSecondary, new(Width - S(22), 0, S(20), Height));
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -2, -2), p.Accent, BackColor);
    }
    private void DrawIcon(Graphics g, string kind, int id, Rectangle rect)
    {
        if (_images.Get(kind, id) is { } image) g.DrawImage(image, rect);
        else { using var brush = new SolidBrush(UiTheme.Palette.SurfaceRaised); g.FillRectangle(brush, rect); }
    }
    public void ApplyTheme(ThemePalette palette) { BackColor = palette.Surface; ForeColor = palette.TextPrimary; Invalidate(); }
    protected override void Dispose(bool disposing) { if (disposing) { _lpStore.Changed -= OnLpChanged; _images.Dispose(); _tip.Dispose(); } base.Dispose(disposing); }
}
