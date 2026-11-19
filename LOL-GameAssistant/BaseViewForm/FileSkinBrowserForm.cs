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
    private CancellationTokenSource? _cardsRequest, _heroesRequest, _applyRequest, _resourceRequest;
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
    private readonly AntdUI.Select _resourceSource = new() { Name = "FileSkinResourceSource", Width = 120, Height = 38, List = true };
    private readonly AntdUI.Button _downloadResources = new() { Name = "FileSkinDownloadResources", Text = "下载资源", Width = 110, Height = 38 };
    private readonly AntdUI.Button _importResources = new() { Name = "FileSkinImportResources", Text = "导入资源包", Width = 130, Height = 38 };
    private readonly AntdUI.Button _checkResources = new() { Name = "FileSkinCheckResourceUpdates", Text = "检查更新", Width = 110, Height = 38 };
    private readonly Panel _header = new() { Name = "FileSkinHeader", Dock = DockStyle.Top, Height = 136, Padding = new Padding(20, 12, 20, 8) };
    private readonly Panel _resourceProgressPanel = new() { Name = "FileSkinResourceProgressPanel", Dock = DockStyle.Top, Height = 44, Visible = false, Padding = new Padding(4, 0, 4, 4) };
    private readonly AntdUI.Label _resourceProgressText = new() { Name = "FileSkinResourceProgressText", Dock = DockStyle.Fill, TextMultiLine = false };
    private readonly AntdUI.Progress _resourceProgressBar = new() { Name = "FileSkinResourceProgressBar", Dock = DockStyle.Bottom, Height = 12, UseSystemText = false, Animation = 0 };
    private FileSkinResourceDownloadProgress? _lastResourceProgress;
    private bool _cancellingResource, _downloadingResource;
    private int _page;
    private readonly UiToolTip _resourceTips = new();
    private bool _checkingResources;
    private DateTime _lastResourceCheck;
    private readonly AntdUI.Label _resourceUpdate = new() { Name = "FileSkinResourceUpdate", Text = "打开页面时检查资源更新", Width = 330, Height = 38, TextMultiLine = false, AutoEllipsis = true };

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
        _stop.Click += async (_, _) => { if (_resourceRequest != null) CancelResourceDownload(); else await ToggleEnabledAsync(false); };
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
        Load += async (_, _) => { await LoadCatalogAsync(); await CheckResourceUpdatesAsync(); };
        _watch.Tick += (_, _) => { SyncEnabled(); UpdateControls(); }; _watch.Start();
        VisibleChanged += async (_, _) => { SyncEnabled(); UpdateControls(); if (Visible && DateTime.UtcNow - _lastResourceCheck > TimeSpan.FromMinutes(10)) await CheckResourceUpdatesAsync(); };
        _cards.Resize += (_, _) => ResizeCards();
        Resize += (_, _) => _body.ColumnStyles[0].Width = Width >= 1200 ? 300 : Width < 850 ? 170 : 260;
        UiTheme.Apply(this); SyncEnabled(); UpdateControls();
    }

    private void BuildLayout()
    {
        var header = _header;
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
        if (_files is IFileSkinResourceInstaller installer)
        {
            header.Height += 44;
            var resources = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, WrapContents = false };
            var download = _downloadResources;
            var check = _checkResources;
            check.Click += async (_, _) => await CheckResourceUpdatesAsync();
            var import = _importResources;
            download.Click += async (_, _) => { if (_resourceRequest != null) CancelResourceDownload(); else await InstallResourcesAsync(installer, null); };
            import.Click += async (_, _) =>
            {
                if (_busy) return;
                using var dialog = new OpenFileDialog { Title = "选择独立换肤资源包", Filter = "换肤资源包 (*.zip)|*.zip" };
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK) await InstallResourcesAsync(installer, dialog.FileName);
            };
            _resourceSource.Items.AddRange(["自动选源", "服务器", "GitHub"]); _resourceSource.SelectedIndex = 0;
            resources.Controls.Add(_resourceSource); resources.Controls.Add(download); resources.Controls.Add(import);
            resources.Controls.Add(check); resources.Controls.Add(_resourceUpdate);
            _resourceProgressPanel.Controls.Add(_resourceProgressText);
            _resourceProgressPanel.Controls.Add(_resourceProgressBar);
            header.Controls.Add(_resourceProgressPanel);
            header.Controls.Add(resources);
        }
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

    private async Task InstallResourcesAsync(IFileSkinResourceInstaller installer, string? archive)
    {
        if (_busy || _disposing) return;
        _busy = true;
        bool installed = false;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _resourceRequest = request;
        _cancellingResource = false; _downloadingResource = archive == null;
        _lastResourceProgress = null;
        _resourceProgressPanel.Visible = _downloadingResource;
        _header.Height = _downloadingResource ? 224 : 180;
        _resourceProgressBar.Value = 0;
        _resourceProgressText.Text = "正在获取资源总大小…";
        UpdateControls();
        try
        {
            if (archive == null)
            {
                var progress = new Progress<FileSkinResourceDownloadProgress>(value =>
                {
                    if (!_disposing && ReferenceEquals(_resourceRequest, request) && !request.IsCancellationRequested)
                        ShowResourceProgress(value);
                });
                await installer.DownloadResourcesAsync(request.Token, _resourceSource.SelectedIndex == 2, progress);
            }
            else await Task.Run(() => installer.ImportResourcesAsync(archive, request.Token));
            installed = true;
            if (!_disposing && _lastResourceProgress is { } finished)
                ShowResourceProgress(finished with { Stage = "资源已安装" });
        }
        catch (OperationCanceledException)
        {
            if (!_disposing)
            {
                _status.Text = archive == null ? "下载已取消，已保留断点，再次点击下载可继续。" : "已取消资源安装。";
                if (_lastResourceProgress is { } cancelled) ShowResourceProgress(cancelled with { Stage = "下载已取消" });
                else _resourceProgressText.Text = "下载已取消";
            }
        }
        catch (Exception error)
        {
            if (!_disposing)
            {
                _status.Text = "资源安装失败：" + error.Message;
                if (_lastResourceProgress is { } failed) ShowResourceProgress(failed with { Stage = "资源获取失败" });
            }
        }
        finally { _resourceRequest = null; _cancellingResource = false; _busy = false; if (!_disposing) UpdateControls(); }
        if (installed && !_disposing) { await LoadCatalogAsync(); await CheckResourceUpdatesAsync(); }
    }

    private void ShowResourceProgress(FileSkinResourceDownloadProgress value)
    {
        _lastResourceProgress = value;
        _resourceProgressText.Text = value.Description;
        _resourceProgressBar.Value = (float)((value.Percent ?? 0) / 100d);
    }

    private void CancelResourceDownload()
    {
        if (_resourceRequest == null || _cancellingResource) return;
        _cancellingResource = true;
        _status.Text = "正在取消，请稍候…";
        _resourceRequest.Cancel();
        UpdateControls();
    }
    internal async Task CheckResourceUpdatesAsync()
    {
        if (_checkingResources || _busy || _disposing || _files is not IFileSkinResourceInstaller installer) return;
        _checkingResources = true; _resourceUpdate.Text = "正在检查资源更新…";
        try
        {
            var update = await installer.CheckForUpdatesAsync(_lifetime.Token);
            if (_disposing || update == null) return;
            string patch = string.IsNullOrWhiteSpace(update.Release.GamePatch) ? "" : " · 游戏 " + update.Release.GamePatch;
            _resourceUpdate.Text = !update.CheckedOnline ? update.Message + " · " + update.Release.Version
                : update.UpdateAvailable ? $"可更新：{update.InstalledVersion} → {update.Release.Version}{patch}"
                : $"资源已是最新：{update.Release.Version}{patch}";
            _resourceUpdate.AccessibleDescription = update.Message + "；" + update.Release.Notes;
            _resourceTips.SetToolTip(_resourceUpdate, _resourceUpdate.Text + "\n" + update.Release.Notes);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_disposing) _resourceUpdate.Text = "资源更新检查失败：" + error.Message; }
        finally { _checkingResources = false; _lastResourceCheck = DateTime.UtcNow; }
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
            var tile = new FileSkinArtTile { Name = "FileSkinCard_" + (entry.ChromaId ?? entry.SkinId.ToString()), Width = 184, Height = 200, Margin = new Padding(8),
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
        int available = Math.Max(166, _cards.ClientSize.Width - 30);
        int columns = Math.Max(1, available / 198);
        int width = Math.Min(210, available / columns - 16);
        foreach (var tile in _skinTiles.Keys) tile.Size = new Size(width, (int)Math.Ceiling((width - 16) * 9d / 16) + 104);
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
        _downloadResources.Text = _resourceRequest == null ? "下载资源" : _cancellingResource ? "正在取消…" : _downloadingResource ? "取消下载" : "取消安装";
        _downloadResources.Enabled = _resourceRequest != null ? !_cancellingResource : !_busy;
        _importResources.Enabled = _checkResources.Enabled = _resourceSource.Enabled = !_busy;
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
        var request = _resourceRequest;
        try { BeginInvoke(() =>
        {
            if (!_disposing && (request == null || ReferenceEquals(request, _resourceRequest) && !request.IsCancellationRequested))
                _status.Text = _names.TranslateStatus(text, _model.Selected);
        }); } catch (InvalidOperationException) { }
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
            _disposing = true; _files.StatusChanged -= Progress; _engines.Changed -= EngineChanged; _watch.Dispose(); _heroSearchDelay.Dispose(); _resourceTips.Dispose(); _lifetime.Cancel();
            _cardsRequest?.Cancel(); _cardsRequest?.Dispose(); _applyRequest?.Cancel(); _lifetime.Dispose();
            _heroesRequest?.Cancel(); _heroesRequest?.Dispose();
        }
        base.Dispose(disposing);
    }
}
