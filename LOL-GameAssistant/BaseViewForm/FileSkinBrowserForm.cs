using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.Skins;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.Skins;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>Out-of-game resource browser. It has no native hero catalog, hotkeys, or in-game overlay state.</summary>
public sealed class FileSkinBrowserForm : UserControl, IThemeAware
{
    private const int PageSize = 16;
    private static readonly Font HeroFont = new(UiMetrics.FontFamily, 8);
    private readonly ICslolSkinService _files;
    private readonly ISkinEngineCoordinator _engines;
    private readonly IApplicationSettingsStore _settings;
    private readonly IFileSkinPreferencesStore _store;
    private readonly IFileSkinArtworkService _artwork;
    private readonly Func<bool> _gameRunning;
    private readonly FileSkinBrowserModel _model = new();
    private readonly CslolSkinNames _names = new();
    private readonly CslolChampionSearch _championSearch = new();
    private static readonly string[] HeroPositions = ["", "TOP", "MIDDLE", "JUNGLE", "BOTTOM", "SUPPORT"];
    private FileSkinPreferences _preferences = new();
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _cardsRequest, _heroesRequest, _applyRequest;
    private readonly AntdUI.Input _path = new() { Name = "FileSkinGamePath", Dock = DockStyle.Fill, PlaceholderText = "选择英雄联盟 Game 目录" };
    private readonly AntdUI.Input _heroQuery = new() { Name = "FileSkinHeroSearch", Dock = DockStyle.Top, Height = 44, PlaceholderText = "英雄 · 中文 / 拼音 / 首字母 / 英文" };
    private readonly AntdUI.Select _heroLane = new() { Name = "FileSkinHeroLane", Dock = DockStyle.Top, Height = 38, List = true };
    private readonly AntdUI.Input _query = new() { Name = "FileSkinSearch", Dock = DockStyle.Fill, PlaceholderText = "在当前英雄中搜索皮肤…" };
    private readonly AntdUI.Select _filter = new() { Name = "FileSkinFilter", Width = 140, Height = 42, List = true };
    private readonly AntdUI.Label _status = new() { Name = "FileSkinStatus", Dock = DockStyle.Top, Height = 30, Text = "加载本地皮肤资源…", TextMultiLine = false };
    private readonly AntdUI.Label _count = new() { Name = "FileSkinCount", Dock = DockStyle.Top, Height = 34 };
    private readonly FlowLayoutPanel _heroes = new() { Name = "FileSkinHeroes", Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(6) };
    private readonly FlowLayoutPanel _cards = new() { Name = "FileSkinCards", Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12, 4, 4, 12) };
    private readonly FlowLayoutPanel _tray = new() { Name = "FileSkinSelectionTray", Dock = DockStyle.Top, Height = 40, AutoScroll = true, WrapContents = false };
    private readonly AntdUI.Label _selection = new() { Name = "FileSkinSelectionCount", Width = 160, Height = 42 };
    private readonly AntdUI.Button _apply = new() { Name = "FileSkinApply", Text = "应用所选皮肤", Width = 174, Height = 44, Type = AntdUI.TTypeMini.Primary };
    private readonly AntdUI.Button _stop = new() { Name = "FileSkinStop", Text = "停止 / 取消", Width = 120, Height = 44 };
    private readonly AntdUI.Button _clear = new() { Name = "FileSkinClear", Text = "清除全部", Width = 100, Height = 42 };
    private readonly AntdUI.Select _presets = new() { Name = "FileSkinPresets", Width = 145, Height = 42, List = true, PlaceholderText = "恢复预设" };
    private readonly AntdUI.Input _presetName = new() { Name = "FileSkinPresetName", Width = 130, Height = 42, Text = "我的预设" };
    private readonly AntdUI.Button _save = new() { Name = "FileSkinSavePreset", Text = "保存预设", Width = 110, Height = 42 };
    private readonly AntdUI.Label _pageLabel = new() { Name = "FileSkinPageNumber", Width = 130, Height = 36, TextAlign = ContentAlignment.MiddleCenter };
    private readonly AntdUI.Button _previous = new() { Name = "FileSkinPreviousPage", Text = "上一页", Width = 90, Height = 36 };
    private readonly AntdUI.Button _next = new() { Name = "FileSkinNextPage", Text = "下一页", Width = 90, Height = 36 };
    private readonly TableLayoutPanel _body = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
    private readonly System.Windows.Forms.Timer _watch = new() { Interval = 2000 };
    private readonly System.Windows.Forms.Timer _heroSearchDelay = new() { Interval = 300 };
    private readonly AntdUI.Switch _enabled = new() { Name = "FileSkinEnabled", Width = 52, Height = 30 };
    private bool _syncingEnabled, _switching;
    private string? _champion;
    private IReadOnlyList<CslolSkinEntry> _filtered = [];
    private readonly Dictionary<FileSkinArtTile, CslolSkinEntry> _skinTiles = [];
    private readonly Dictionary<FileSkinArtTile, string> _heroTiles = [];
    private bool _loaded, _busy, _disposing;
    private int _page;

    public FileSkinBrowserForm(ICslolSkinService files, ISkinEngineCoordinator engines, IApplicationSettingsStore settings,
        IFileSkinPreferencesStore preferences, IFileSkinArtworkService artwork, Func<bool>? gameRunning = null)
    {
        _files = files; _engines = engines; _settings = settings; _store = preferences; _artwork = artwork;
        _gameRunning = gameRunning ?? CslolSkinService.IsGameRunning;
        Dock = DockStyle.Fill; Font = new Font(UiMetrics.FontFamily, 10); Name = "FileSkinBrowser";
        BuildLayout();
        _heroQuery.TextChanged += (_, _) => { _heroSearchDelay.Stop(); _heroSearchDelay.Start(); };
        _heroSearchDelay.Tick += (_, _) => { _heroSearchDelay.Stop(); FilterHeroes(); };
        _heroLane.Items.AddRange(["全部分路", "上单", "中单", "打野", "下路", "辅助"]); _heroLane.SelectedIndex = 0;
        _heroLane.SelectedIndexChanged += (_, _) => { _heroSearchDelay.Stop(); FilterHeroes(); };
        _query.TextChanged += (_, _) => FilterCards();
        _filter.Items.AddRange(["仅皮肤", "含炫彩 / 形态", "仅收藏", "仅已选择"]); _filter.SelectedIndex = 0;
        _filter.SelectedIndexChanged += (_, _) => FilterCards();
        _clear.Click += (_, _) => { _model.Clear(); RefreshSelection(); FilterCards(); };
        _previous.Click += (_, _) => { _page--; RenderCards(); };
        _next.Click += (_, _) => { _page++; RenderCards(); };
        _apply.Click += async (_, _) => await ApplyAsync();
        _stop.Click += async (_, _) => await ToggleEnabledAsync(false);
        _enabled.CheckedChanged += async (_, _) => { if (!_syncingEnabled) await ToggleEnabledAsync(_enabled.Checked); };
        _engines.Changed += EngineChanged;
        _save.Click += (_, _) => SavePreset();
        _presets.SelectedIndexChanged += (_, _) =>
        {
            if (_presets.SelectedIndex < 0) return;
            var key = _presets.Items[_presets.SelectedIndex]?.ToString() ?? "";
            if (_preferences.Presets.TryGetValue(key, out var picks)) { _model.Restore(picks); RefreshSelection(); FilterCards(); }
        };
        _files.StatusChanged += Progress;
        Load += async (_, _) => await LoadCatalogAsync();
        _watch.Tick += (_, _) => { SyncEnabled(); UpdateControls(); }; _watch.Start();
        VisibleChanged += (_, _) => { SyncEnabled(); UpdateControls(); };
        _cards.Resize += (_, _) => ResizeCards();
        Resize += (_, _) => _body.ColumnStyles[0].Width = Width >= 1200 ? 300 : Width < 850 ? 170 : 260;
        UiTheme.Apply(this); SyncEnabled(); UpdateControls();
    }

    private void BuildLayout()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 136, Padding = new Padding(20, 12, 20, 8) };
        var title = new TableLayoutPanel { Dock = DockStyle.Top, Height = 36, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        title.RowStyles.Add(new(SizeType.Percent, 100));
        title.ColumnStyles.Add(new(SizeType.Percent, 100)); title.ColumnStyles.Add(new(SizeType.Absolute, 210));
        title.Controls.Add(new AntdUI.Label { Dock = DockStyle.Fill, Text = "游戏外换肤", Font = new Font(UiMetrics.FontFamily, 15, FontStyle.Bold) }, 0, 0);
        var toggleRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        toggleRow.Controls.Add(new AntdUI.Label { Text = "启用游戏外换肤", Width = 140, Height = 30, Margin = Padding.Empty }); toggleRow.Controls.Add(_enabled);
        title.Controls.Add(toggleRow, 1, 0);
        var tools = new TableLayoutPanel { Dock = DockStyle.Top, Height = 46, ColumnCount = 3 };
        tools.ColumnStyles.Add(new(SizeType.Percent, 100)); tools.ColumnStyles.Add(new(SizeType.Absolute, 120)); tools.ColumnStyles.Add(new(SizeType.Absolute, 110));
        var browse = new AntdUI.Button { Text = "选择目录", Dock = DockStyle.Fill };
        var reload = new AntdUI.Button { Name = "FileSkinReload", Text = "刷新资源", Dock = DockStyle.Fill };
        browse.Click += (_, _) => { if (_busy) return; using var dialog = new FolderBrowserDialog { Description = "选择英雄联盟 Game 目录" };
            if (dialog.ShowDialog(FindForm()) == DialogResult.OK) _path.Text = CslolSkinService.ResolveGameDirectory(dialog.SelectedPath) ?? dialog.SelectedPath; };
        reload.Click += async (_, _) => await LoadCatalogAsync();
        var config = _settings.Load(); _path.Text = CslolSkinService.ResolveGameDirectory(config.CslolGamePath) ?? CslolSkinService.ResolveGameDirectory(config.GameClientPath) ?? config.CslolGamePath;
        tools.Controls.Add(_path, 0, 0); tools.Controls.Add(browse, 1, 0); tools.Controls.Add(reload, 2, 0);
        header.Controls.Add(_status); header.Controls.Add(tools); header.Controls.Add(title);
        _body.ColumnStyles.Add(new(SizeType.Absolute, 260)); _body.ColumnStyles.Add(new(SizeType.Percent, 100));
        _body.RowStyles.Add(new(SizeType.Percent, 100));
        var sidebar = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), Name = "FileSkinSidebar" };
        var all = new AntdUI.Button { Name = "FileSkinAllHeroes", Dock = DockStyle.Top, Height = 38, Text = "全部英雄 · 跨英雄搜索" };
        all.Click += (_, _) => { _champion = null; _query.PlaceholderText = "在所有英雄中搜索皮肤…"; FilterCards(); HighlightHeroes(); };
        sidebar.Controls.Add(_heroes); sidebar.Controls.Add(all); sidebar.Controls.Add(_heroLane); sidebar.Controls.Add(_heroQuery);
        var gallery = new Panel { Dock = DockStyle.Fill };
        var filters = new TableLayoutPanel { Dock = DockStyle.Top, Height = 60, ColumnCount = 2, Padding = new Padding(16, 8, 16, 8) };
        filters.ColumnStyles.Add(new(SizeType.Percent, 100)); filters.ColumnStyles.Add(new(SizeType.Absolute, 150));
        filters.Controls.Add(_query, 0, 0); filters.Controls.Add(_filter, 1, 0);
        var pagination = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 4, 12, 4) };
        pagination.Controls.AddRange([_next, _pageLabel, _previous]);
        _count.Padding = new Padding(18, 0, 0, 0);
        gallery.Controls.Add(_cards); gallery.Controls.Add(pagination); gallery.Controls.Add(_count); gallery.Controls.Add(filters);
        _body.Controls.Add(sidebar, 0, 0); _body.Controls.Add(gallery, 1, 0);
        var footer = new Panel { Name = "FileSkinFooter", Dock = DockStyle.Bottom, Height = 110, Padding = new Padding(18, 8, 18, 8) };
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        actions.ColumnStyles.Add(new(SizeType.Percent, 100)); actions.ColumnStyles.Add(new(SizeType.Absolute, 320));
        var choices = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
        var run = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.RightToLeft };
        choices.Controls.AddRange([_selection, _clear, _presetName, _save, _presets]); run.Controls.AddRange([_apply, _stop]);
        actions.Controls.Add(choices, 0, 0); actions.Controls.Add(run, 1, 0);
        footer.Controls.Add(actions); footer.Controls.Add(_tray);
        Controls.Add(_body); Controls.Add(footer); Controls.Add(header);
    }

    internal async Task LoadCatalogAsync()
    {
        if (_busy || _disposing) return;
        _busy = true; UpdateControls();
        try
        {
            _model.SetCatalog(await Task.Run(() => _files.LoadCatalogAsync(_lifetime.Token)));
            if (_disposing) return;
            _preferences = _store.Load(); _loaded = true;
            _champion ??= _model.Catalog.FirstOrDefault()?.ChampionKey;
            RenderHeroes(); FilterCards(); RefreshSelection(); RefreshPresets();
            _status.Text = $"本地资源 {_model.Catalog.Count:N0} 个 · 版本 {_files.CatalogPatch} · 应用后再进入游戏";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_disposing) _status.Text = "资源加载失败：" + error.Message; }
        finally { _busy = false; if (!_disposing) UpdateControls(); }
    }
    private void RenderHeroes()
    {
        if (_disposing) return;
        _heroSearchDelay.Stop();
        _heroesRequest?.Cancel(); _heroesRequest?.Dispose(); _heroesRequest = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _heroes.SuspendLayout();
        try
        {
            foreach (Control control in _heroes.Controls.Cast<Control>().ToArray()) control.Dispose();
            _heroTiles.Clear();
            var query = _heroQuery.Text.Trim();
            foreach (var entry in _model.Catalog.DistinctBy(e => e.ChampionKey))
            {
                var tile = new FileSkinArtTile { Name = "FileHero_" + entry.ChampionKey, Compact = true, Width = 56, Height = 84, Margin = new Padding(2),
                    Font = HeroFont,
                    Caption = _names.Champion(entry.ChampionKey), AccessibleName = _names.Champion(entry.ChampionKey), Selected = entry.ChampionKey == _champion,
                    Visible = MatchesHero(entry.ChampionKey, query) };
                tile.SelectedByUser += () => { _champion = entry.ChampionKey; _query.PlaceholderText = $"在{_names.Champion(_champion)}的皮肤中搜索…"; HighlightHeroes(); FilterCards(); };
                tile.ApplyTheme(UiTheme.Palette); _heroTiles[tile] = entry.ChampionKey; _heroes.Controls.Add(tile);
                _ = LoadArtAsync(tile, entry, true, _heroesRequest.Token);
            }
        }
        finally { _heroes.ResumeLayout(true); }
    }
    private void FilterHeroes()
    {
        if (_disposing) return;
        var query = _heroQuery.Text.Trim();
        // Keep controls and decoded artwork alive while typing; only their visibility changes.
        _heroes.SuspendLayout();
        try
        {
            _heroes.AutoScrollPosition = Point.Empty;
            foreach (var pair in _heroTiles)
                pair.Key.Visible = MatchesHero(pair.Value, query);
        }
        finally { _heroes.ResumeLayout(true); }
    }
    private bool MatchesHero(string key, string query) => _championSearch.Matches(key, _names.Champion(key), query,
        HeroPositions[Math.Clamp(_heroLane.SelectedIndex, 0, HeroPositions.Length - 1)]);
    private void HighlightHeroes() { foreach (var pair in _heroTiles) { pair.Key.Selected = pair.Value == _champion; pair.Key.Invalidate(); } }
    private void FilterCards()
    {
        _page = 0;
        _filtered = _model.Filter(_champion, e => _names.Matches(e, _query.Text), _preferences.Favorites,
            _filter.SelectedIndex == 2, _filter.SelectedIndex == 3, _filter.SelectedIndex != 0);
        RenderCards();
    }
    private void RenderCards()
    {
        _cardsRequest?.Cancel(); _cardsRequest?.Dispose(); _cardsRequest = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        foreach (Control control in _cards.Controls.Cast<Control>().ToArray()) control.Dispose(); _skinTiles.Clear();
        int pages = Math.Max(1, (_filtered.Count + PageSize - 1) / PageSize); _page = Math.Clamp(_page, 0, pages - 1);
        _count.Text = $"{(_champion is null ? "全部英雄" : _names.Champion(_champion))}  ·  {_filtered.Count} 个皮肤  ·  每个英雄可选一款";
        foreach (var entry in _filtered.Skip(_page * PageSize).Take(PageSize))
        {
            var tile = new FileSkinArtTile { Name = "FileSkinCard_" + (entry.ChromaId ?? entry.SkinId.ToString()), Width = 184, Height = 300, Margin = new Padding(8),
                Caption = _names.Display(entry), Badge = _names.Champion(entry.ChampionKey), Selected = _model.IsSelected(entry),
                Favorite = _preferences.Favorites.Contains(entry.FileName), AccessibleName = _names.Display(entry), Tag = entry };
            tile.SelectedByUser += () => { if (_busy) return; _model.Toggle(entry); RefreshSelection(); if (_filter.SelectedIndex == 3) FilterCards();
                else foreach (var pair in _skinTiles) { pair.Key.Selected = _model.IsSelected(pair.Value); pair.Key.Invalidate(); } };
            tile.FavoriteByUser += () => { if (_busy) return; if (!_preferences.Favorites.Add(entry.FileName)) _preferences.Favorites.Remove(entry.FileName);
                tile.Favorite = _preferences.Favorites.Contains(entry.FileName); tile.Invalidate(); SavePreferences(); if (_filter.SelectedIndex == 2) FilterCards(); };
            tile.ApplyTheme(UiTheme.Palette); _cards.Controls.Add(tile); _skinTiles[tile] = entry;
            _ = LoadArtAsync(tile, entry, false, _cardsRequest.Token);
        }
        if (_filtered.Count == 0) _cards.Controls.Add(new AntdUI.Label { Text = "没有匹配的皮肤，试试其他关键词或筛选。", Width = 420, Height = 100 });
        _pageLabel.Text = $"{_page + 1} / {pages}"; _previous.Enabled = _page > 0; _next.Enabled = _page + 1 < pages;
        ResizeCards();
    }
    private void ResizeCards()
    {
        int available = Math.Max(190, _cards.ClientSize.Width - 30);
        int columns = Math.Max(1, available / 218);
        int width = Math.Min(245, available / columns - 16);
        foreach (var tile in _skinTiles.Keys) tile.Size = new Size(width, (int)(width * 1.32) + 64);
    }
    private async Task LoadArtAsync(FileSkinArtTile tile, CslolSkinEntry entry, bool hero, CancellationToken token)
    {
        try
        {
            var asset = hero ? await _artwork.GetChampionAsync(entry, token) : await _artwork.GetSkinAsync(entry, token);
            if (tile.IsDisposed || _disposing || token.IsCancellationRequested || asset is not { IsEmpty: false }) return;
            using var stream = new MemoryStream(asset.Content); using var image = Image.FromStream(stream); tile.SetArtwork(new Bitmap(image));
        }
        catch (OperationCanceledException) { } catch { /* Keep the named placeholder usable without artwork/network. */ }
    }
    private void RefreshSelection()
    {
        foreach (Control control in _tray.Controls.Cast<Control>().ToArray()) control.Dispose();
        foreach (var entry in _model.Selected)
        {
            var chip = new AntdUI.Button { Text = _names.Display(entry) + " ×", AutoSizeMode = AntdUI.TAutoSize.Auto, Height = 32, Margin = new Padding(0, 0, 8, 0) };
            chip.Click += (_, _) => { if (_busy) return; _model.Toggle(entry); RefreshSelection(); FilterCards(); }; _tray.Controls.Add(chip);
        }
        _selection.Text = $"已选择 {_model.Selected.Count} 个英雄";
        _apply.Text = $"应用 {_model.Selected.Count} 个皮肤"; UpdateControls();
    }
    private void SavePreset()
    {
        string name = _presetName.Text.Trim();
        if (name.Length == 0 || name.Length > 40) { _status.Text = "请输入 1–40 字的预设名称"; return; }
        _preferences.Presets[name] = _model.Selected.Select(e => e.FileName).ToArray(); SavePreferences(); RefreshPresets(); _status.Text = $"已保存预设：{name}";
    }
    private void RefreshPresets() { _presets.Items.Clear(); foreach (string key in _preferences.Presets.Keys) _presets.Items.Add(key); _presets.SelectedIndex = -1; }
    private void SavePreferences() { try { _store.Save(_preferences); } catch (Exception error) { _status.Text = "保存失败：" + error.Message; } }
    private async Task ApplyAsync()
    {
        if (_busy || !_loaded || _model.Selected.Count == 0) return;
        var picks = _model.Selected; _busy = true; UpdateControls();
        _applyRequest = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        try
        {
            if (!_engines.IsEnabled(SkinEngineMode.File)) throw new InvalidOperationException("请先开启游戏外换肤开关");
            var config = _settings.Load(); config.CslolGamePath = _path.Text.Trim(); _settings.Save(config);
            var result = await _files.ApplyAsync(config.CslolGamePath, picks, cancellationToken: _applyRequest.Token);
            if (!_disposing) _status.Text = result.Ok ? "已准备完成，请进入游戏。" : $"应用失败（{result.Stage}）：{_names.TranslateStatus(result.Message, picks)}";
        }
        catch (OperationCanceledException) { if (!_disposing) _status.Text = "已取消"; }
        catch (Exception error) { if (!_disposing) _status.Text = error.Message; }
        finally { _applyRequest.Dispose(); _applyRequest = null; _busy = false; if (!_disposing) UpdateControls(); }
    }
    private void UpdateControls()
    {
        if (_disposing) return;
        bool running = _gameRunning();
        _apply.Enabled = _engines.IsEnabled(SkinEngineMode.File) && _loaded && !_busy && !_switching && !running && _files.IsAvailable && _model.Selected.Count > 0;
        _enabled.Enabled = !_switching;
        _stop.Enabled = _busy || _files.IsRunning; _clear.Enabled = _save.Enabled = _presets.Enabled = !_busy;
        _heroes.Enabled = _cards.Enabled = _path.Enabled = _filter.Enabled = _heroLane.Enabled = !_busy;
        if (running && !_busy) _status.Text = !CslolSkinService.HasGameWindow()
            ? "检测到游戏进程，但尚无游戏窗口。若持续无法进入，请点击停止 / 取消，退出游戏后重试。"
            : _files.IsRunning ? "已检测到游戏窗口 · 文件换肤进程运行中，新的选择需下局应用。" : "已检测到游戏窗口：可以浏览和选择，退出对局后才能应用。";
    }
    private void SyncEnabled()
    {
        _syncingEnabled = true; try { _enabled.Checked = _engines.IsEnabled(SkinEngineMode.File); } finally { _syncingEnabled = false; }
    }
    private void EngineChanged()
    {
        if (_disposing || IsDisposed) return;
        if (InvokeRequired) { try { BeginInvoke(EngineChanged); } catch (InvalidOperationException) { } return; }
        SyncEnabled(); UpdateControls();
    }
    private async Task ToggleEnabledAsync(bool enabled)
    {
        if (_switching) return;
        _switching = true; if (!enabled) _applyRequest?.Cancel(); UpdateControls();
        try
        {
            await _engines.SetEnabledAsync(SkinEngineMode.File, enabled, _lifetime.Token);
            if (!_disposing) _status.Text = enabled ? "游戏外换肤已启用，其他换肤功能已关闭。" : "游戏外换肤已关闭，覆盖进程已停止。";
        }
        catch (Exception error) { if (!_disposing) _status.Text = error.Message; }
        finally { _switching = false; if (!_disposing) { SyncEnabled(); UpdateControls(); } }
    }
    private void Progress(string text)
    {
        if (_disposing || !IsHandleCreated) return;
        try { BeginInvoke(() => { if (!_disposing) _status.Text = _names.TranslateStatus(text, _model.Selected); }); } catch (InvalidOperationException) { }
    }
    public void ApplyTheme(ThemePalette palette)
    {
        AntdContentTheme.Apply(this, palette); BackColor = palette.Surface;
        void ThemePanels(Control control)
        {
            foreach (Control child in control.Controls)
            {
                if (child is Panel or TableLayoutPanel or FlowLayoutPanel) child.BackColor = palette.SurfaceRaised;
                if (child is AntdUI.Label) child.BackColor = palette.SurfaceRaised;
                ThemePanels(child);
            }
        }
        ThemePanels(this);
        _body.BackColor = _cards.BackColor = palette.SurfaceMuted; _heroes.BackColor = palette.SurfaceRaised;
        _count.BackColor = palette.SurfaceMuted;
        _status.ForeColor = palette.TextSecondary;
        foreach (var tile in _skinTiles.Keys.Concat(_heroTiles.Keys)) tile.ApplyTheme(palette);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposing)
        {
            _disposing = true; _files.StatusChanged -= Progress; _engines.Changed -= EngineChanged; _watch.Dispose(); _heroSearchDelay.Dispose(); _lifetime.Cancel();
            _cardsRequest?.Cancel(); _cardsRequest?.Dispose(); _applyRequest?.Cancel(); _lifetime.Dispose();
            _heroesRequest?.Cancel(); _heroesRequest?.Dispose();
        }
        base.Dispose(disposing);
    }
}
