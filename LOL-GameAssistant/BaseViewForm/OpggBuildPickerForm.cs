using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.GameData;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>
/// OP.GG 图文出装/符文选择框。只展示公开推荐；用户明确点击应用后，调用方才会写入 LCU。
/// </summary>
internal sealed class OpggBuildPickerForm : Form, IThemeAware
{
    private readonly OpggBuildChoices _choices;
    private readonly IGameAssetService _gameAssetService;
    private readonly FlowLayoutPanel _routeCards = new();
    private readonly Button _apply = new() { Text = "应用选中方案", AutoSize = true, Enabled = false };
    private readonly Label _title = new();
    private readonly Label _note = new();
    private readonly Label _modeBadge = new();
    private readonly Label _sourceHint = new();
    private readonly Panel _heroHeader = new();
    private readonly PictureBox _championIcon = new();
    private readonly List<RouteCard> _cards = new();
    private readonly List<Image> _ownedImages = new();
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 7000, InitialDelay = 250, ReshowDelay = 100 };
    private readonly SemaphoreSlim _visualLoadGate = new(6, 6);
    private RouteCard? _selectedCard;

    public OpggBuildOption? SelectedOption => _selectedCard?.Option;

    public OpggBuildPickerForm(
        OpggBuildChoices choices,
        IGameAssetService gameAssetService,
        int initiallySelectedOrder = 0)
    {
        _choices = choices;
        _gameAssetService = gameAssetService;
        Text = $"OP.GG 方案选择 · {choices.ChampionName} {choices.PositionName}";
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = true;
        MinimizeBox = false;
        ShowInTaskbar = false;
        // 选人阶段英雄联盟客户端在前台，而本窗口不占任务栏：
        // 不置顶的话它可能开在客户端后面且完全看不出来。
        TopMost = true;
        MinimumSize = new Size(860, 600);
        ClientSize = new Size(1080, 760);
        Padding = new Padding(18);
        Font = new Font("Microsoft YaHei UI", 9F);

        BuildUi(initiallySelectedOrder);
        UiTheme.Apply(this);
        Shown += async (_, _) =>
        {
            Activate();
            BringToFront();
            await UpdateRecommendationNoteAsync();
            await LoadVisualsAsync();
        };
        FormClosed += (_, _) => DisposeOwnedImages();
    }

    private void BuildUi(int initiallySelectedOrder)
    {
        _heroHeader.Dock = DockStyle.Top;
        _heroHeader.Height = 126;
        _heroHeader.Padding = new Padding(14, 12, 14, 12);
        _championIcon.Size = new Size(72, 72);
        _championIcon.SizeMode = PictureBoxSizeMode.Zoom;
        _championIcon.Dock = DockStyle.Left;

        var headerText = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(16, 0, 0, 0) };
        headerText.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        headerText.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        headerText.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _title.Dock = DockStyle.Fill;
        _title.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);
        _title.Text = $"{_choices.ChampionName} · 当前对局推荐";
        _note.Dock = DockStyle.Fill;
        _note.Font = new Font("Microsoft YaHei UI", 9F);
        _note.Text = BuildRecommendationNote();
        _modeBadge.AutoSize = true;
        _modeBadge.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        _modeBadge.Padding = new Padding(8, 3, 8, 2);
        _modeBadge.Text = $"当前模式：{_choices.PositionName}";
        _sourceHint.AutoSize = true;
        _sourceHint.TextAlign = ContentAlignment.MiddleLeft;
        _sourceHint.Padding = new Padding(12, 0, 0, 0);
        _sourceHint.Text = $"OP.GG 公开数据 · {_choices.Options.Count} 套方案 · 选择后点击应用";
        var contextRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        contextRow.Controls.Add(_modeBadge);
        contextRow.Controls.Add(_sourceHint);
        headerText.Controls.Add(_title, 0, 0);
        headerText.Controls.Add(_note, 0, 1);
        headerText.Controls.Add(contextRow, 0, 2);
        _heroHeader.Controls.Add(headerText);
        _heroHeader.Controls.Add(_championIcon);

        _routeCards.Dock = DockStyle.Fill;
        _routeCards.AutoScroll = true;
        _routeCards.FlowDirection = FlowDirection.TopDown;
        _routeCards.WrapContents = false;
        _routeCards.Padding = new Padding(0, 16, 8, 4);
        _routeCards.SizeChanged += (_, _) => ResizeRouteCards();

        foreach (OpggBuildOption option in _choices.Options)
        {
            RouteCard card = CreateRouteCard(option);
            _cards.Add(card);
            _routeCards.Controls.Add(card.Root);
        }
        if (_cards.Count > 0)
            SelectCard(_cards.FirstOrDefault(card => card.Option.Order == initiallySelectedOrder) ?? _cards[0]);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 62,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 13, 0, 0),
            WrapContents = false
        };
        var cancel = new Button { Text = "暂不应用", AutoSize = true, DialogResult = DialogResult.Cancel };
        _apply.Click += (_, _) => ConfirmSelection();
        footer.Controls.Add(cancel);
        footer.Controls.Add(_apply);

        Controls.Add(_routeCards);
        Controls.Add(footer);
        Controls.Add(_heroHeader);
        AcceptButton = _apply;
        CancelButton = cancel;
    }

    private string BuildRecommendationNote()
    {
        int spellCount = _choices.Options.FirstOrDefault()?.SummonerSpellIds?.Count ?? 0;
        string spells = spellCount >= 2 ? "含推荐召唤师技能" : "该模式未提供可写入的召唤师技能";
        string augments = _choices.Augments?.Count > 0 ? "正在整理海克斯推荐" : "暂无海克斯数据";
        string matchups = _choices.Matchups?.Count > 0
            ? "正在整理对位推荐"
            : "暂无对位数据";
        return $"{spells}。{augments}；{matchups}。";
    }

    private async Task UpdateRecommendationNoteAsync()
    {
        int spellCount = _choices.Options.FirstOrDefault()?.SummonerSpellIds?.Count ?? 0;
        string spells = spellCount >= 2 ? "含推荐召唤师技能" : "当前模式无可写入技能";
        var augments = _choices.Augments?.Take(3).ToArray() ?? [];
        var names = await AugmentCatalog.ResolveAsync(augments.Select(item => item.Id));
        string augmentText = augments.Length == 0 ? "无海克斯统计" :
            "海克斯：" + string.Join("、", names.Select((name, index) =>
                $"{name.Name} {augments[index].WinRate:F1}%"));
        var matchups = _choices.Matchups?.Take(3).ToArray() ?? [];
        string matchupText = matchups.Length == 0 ? "无对位统计" :
            "对位：" + string.Join("、", matchups.Select(item =>
                $"{AppCompositionRoot.ChampionCatalog.GetDisplayName(item.ChampionId)} {item.WinRate:F1}%"));
        if (!IsDisposed) _note.Text = $"{spells}。{augmentText}；{matchupText}。";
    }

    private RouteCard CreateRouteCard(OpggBuildOption option)
    {
        var root = new Panel
        {
            Height = 244,
            Width = 1000,
            Margin = new Padding(0, 0, 0, 14),
            Padding = new Padding(16),
            Cursor = Cursors.Hand
        };
        var indicator = new Panel { Dock = DockStyle.Left, Width = 5, Margin = new Padding(0, 0, 10, 0) };
        var content = new Panel { Dock = DockStyle.Fill };

        var cardHeader = new Panel { Dock = DockStyle.Top, Height = 42 };
        var label = new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
            Text = option.Order == 1 ? "方案 1 · 热门路线" : $"方案 {option.Order} · 备选路线"
        };
        var sample = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Text = $"{option.Matches:N0} 场样本",
            Padding = new Padding(12, 4, 0, 0)
        };
        var winRate = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
            Text = $"胜率 {option.WinRate:F1}%",
            Padding = new Padding(0, 2, 0, 0)
        };
        cardHeader.Resize += (_, _) =>
        {
            sample.Left = cardHeader.ClientSize.Width - sample.Width;
            winRate.Left = sample.Left - winRate.Width - 14;
        };
        cardHeader.Controls.Add(label);
        cardHeader.Controls.Add(winRate);
        cardHeader.Controls.Add(sample);

        var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(0, 2, 0, 0) };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 59));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 41));

        var itemPanel = new Panel { Dock = DockStyle.Fill };
        var itemTitle = new Label { Dock = DockStyle.Top, Height = 24, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold), Text = "装备路线   起始 / 核心 / 可选" };
        var itemSlots = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true, Padding = new Padding(0, 2, 0, 0) };
        var itemTileList = new List<AssetTile>();
        AddItemSection(itemSlots, "起始", option.StarterItemIds, itemTileList);
        AddItemSection(itemSlots, "核心", option.CoreItemIds, itemTileList);
        AddItemSection(itemSlots, "备选", option.SituationalItemIds.Take(2), itemTileList);
        itemPanel.Controls.Add(itemSlots);
        itemPanel.Controls.Add(itemTitle);

        var runePanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 0, 0) };
        var runeTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            Text = "符文配置   主系 / 副系 / 属性"
        };
        var runeSlots = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true, Padding = new Padding(0, 2, 0, 0) };
        var runeTileList = new List<AssetTile>();
        foreach (int runeId in option.RunePerkIds)
        {
            AssetTile tile = CreateAssetTile(runeId, size: 31, labelHeight: 0);
            tile.Root.Width = 38;
            tile.Root.Height = 38;
            tile.Icon.Location = new Point(3, 2);
            runeTileList.Add(tile);
            runeSlots.Controls.Add(tile.Root);
        }
        runePanel.Controls.Add(runeSlots);
        runePanel.Controls.Add(runeTitle);

        columns.Controls.Add(itemPanel, 0, 0);
        columns.Controls.Add(runePanel, 1, 0);
        content.Controls.Add(columns);
        content.Controls.Add(cardHeader);
        root.Controls.Add(content);
        root.Controls.Add(indicator);

        var card = new RouteCard(option, root, indicator, label, winRate, sample, itemTitle, runeTitle, itemTileList, runeTileList);
        AttachSelectionHandler(root, card);
        return card;
    }

    private void AddItemSection(
        FlowLayoutPanel host,
        string title,
        IEnumerable<int> itemIds,
        ICollection<AssetTile> tiles)
    {
        int[] ids = itemIds.Where(id => id > 0).ToArray();
        if (ids.Length == 0) return;

        host.Controls.Add(new Label
        {
            AutoSize = true,
            Text = title,
            Padding = new Padding(2, 18, 4, 0),
            ForeColor = UiTheme.Palette.TextSecondary
        });
        foreach (int itemId in ids)
        {
            AssetTile tile = CreateAssetTile(itemId, size: 48, labelHeight: 30);
            tiles.Add(tile);
            host.Controls.Add(tile.Root);
        }
    }

    private AssetTile CreateAssetTile(int id, int size, int labelHeight)
    {
        var root = new Panel { Width = Math.Max(size + 12, 76), Height = size + labelHeight + 4, Margin = new Padding(0, 0, 8, 0), Cursor = Cursors.Hand };
        var icon = new PictureBox
        {
            Size = new Size(size, size),
            Location = new Point(Math.Max(0, (root.Width - size) / 2), 0),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent
        };
        var name = new Label
        {
            AutoEllipsis = true,
            AutoSize = false,
            TextAlign = ContentAlignment.TopCenter,
            Location = new Point(0, size + 2),
            Size = new Size(root.Width, labelHeight),
            Text = labelHeight > 0 ? "加载装备…" : ""
        };
        root.Controls.Add(name);
        root.Controls.Add(icon);
        return new AssetTile(id, root, icon, name);
    }

    private void AttachSelectionHandler(Control control, RouteCard card)
    {
        control.Click += (_, _) => SelectCard(card);
        foreach (Control child in control.Controls) AttachSelectionHandler(child, card);
    }

    private void SelectCard(RouteCard card)
    {
        _selectedCard = card;
        _apply.Enabled = true;
        ApplyTheme(UiTheme.Palette);
    }

    private void ResizeRouteCards()
    {
        int width = Math.Max(300, _routeCards.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 18);
        foreach (RouteCard card in _cards) card.Root.Width = width;
    }

    private async Task LoadVisualsAsync()
    {
        var tasks = new List<Task> { LoadChampionVisualAsync() };
        foreach (RouteCard card in _cards)
        {
            foreach (AssetTile tile in card.ItemTiles) tasks.Add(LoadItemVisualAsync(tile));
            foreach (AssetTile tile in card.RuneTiles) tasks.Add(LoadRuneVisualAsync(tile));
        }
        await Task.WhenAll(tasks);
    }

    private async Task LoadChampionVisualAsync()
    {
        try
        {
            await _visualLoadGate.WaitAsync();
            try
            {
                GameAsset? asset = await _gameAssetService.GetChampionIconAsync(ResolveChampionId());
                AssignImage(_championIcon, ToImage(asset));
            }
            finally
            {
                _visualLoadGate.Release();
            }
        }
        catch
        {
            // 英雄图标不可用时标题仍会完整显示。
        }
    }

    private async Task LoadItemVisualAsync(AssetTile tile)
    {
        try
        {
            await _visualLoadGate.WaitAsync();
            try
            {
                Task<GameAsset?> iconTask = _gameAssetService.GetItemIconAsync(tile.Id);
                Task<string?> nameTask = _gameAssetService.GetItemNameAsync(tile.Id);
                await Task.WhenAll(iconTask, nameTask);
                string name = nameTask.Result ?? $"装备 {tile.Id}";
                tile.Name.Text = name;
                _toolTip.SetToolTip(tile.Root, name);
                _toolTip.SetToolTip(tile.Icon, name);
                AssignImage(tile.Icon, ToImage(iconTask.Result));
            }
            finally
            {
                _visualLoadGate.Release();
            }
        }
        catch
        {
            tile.Name.Text = $"装备 {tile.Id}";
            _toolTip.SetToolTip(tile.Root, tile.Name.Text);
        }
    }

    private async Task LoadRuneVisualAsync(AssetTile tile)
    {
        try
        {
            await _visualLoadGate.WaitAsync();
            try
            {
                AssignImage(tile.Icon, ToImage(await _gameAssetService.GetRuneIconAsync(tile.Id)));
                _toolTip.SetToolTip(tile.Root, $"符文 {tile.Id}");
            }
            finally
            {
                _visualLoadGate.Release();
            }
        }
        catch
        {
            _toolTip.SetToolTip(tile.Root, $"符文 {tile.Id}");
        }
    }

    private int ResolveChampionId() => _choices.ChampionId > 0
        ? _choices.ChampionId
        : AppCompositionRoot.ChampionCatalog.FindIdByDisplayName(_choices.ChampionName) ?? 0;

    private void AssignImage(PictureBox target, Image? image)
    {
        if (image == null) return;
        if (IsDisposed || target.IsDisposed)
        {
            image.Dispose();
            return;
        }
        Image? oldImage = target.Image;
        target.Image = image;
        if (oldImage != null)
        {
            _ownedImages.Remove(oldImage);
            oldImage.Dispose();
        }
        _ownedImages.Add(image);
    }

    private static Image? ToImage(GameAsset? asset)
    {
        try
        {
            if (asset == null || asset.IsEmpty) return null;
            using var stream = new MemoryStream(asset.Content);
            using var source = Image.FromStream(stream);
            return new Bitmap(source);
        }
        catch
        {
            return null;
        }
    }

    private void ConfirmSelection()
    {
        if (SelectedOption == null) return;
        DialogResult = DialogResult.OK;
        Close();
    }

    public void ApplyTheme(ThemePalette palette)
    {
        BackColor = palette.Surface;
        ForeColor = palette.TextPrimary;
        _title.ForeColor = palette.TextPrimary;
        _note.ForeColor = palette.TextSecondary;
        _heroHeader.BackColor = palette.SurfaceRaised;
        _modeBadge.BackColor = palette.IsDark ? Color.FromArgb(30, 53, 78) : Color.FromArgb(232, 243, 253);
        _modeBadge.ForeColor = palette.Accent;
        _sourceHint.ForeColor = palette.TextSecondary;
        foreach (RouteCard card in _cards)
        {
            bool selected = ReferenceEquals(card, _selectedCard);
            card.Root.BackColor = selected
                ? (palette.IsDark ? Color.FromArgb(30, 53, 78) : Color.FromArgb(232, 243, 253))
                : palette.SurfaceRaised;
            card.Indicator.BackColor = selected ? palette.Accent : palette.Border;
            card.Label.ForeColor = selected ? palette.Accent : palette.TextPrimary;
            card.WinRate.ForeColor = selected ? palette.Accent : palette.TextPrimary;
            card.Sample.ForeColor = palette.TextSecondary;
            card.ItemTitle.ForeColor = palette.TextSecondary;
            card.RuneTitle.ForeColor = palette.TextSecondary;
            foreach (AssetTile tile in card.ItemTiles.Concat(card.RuneTiles))
            {
                tile.Name.ForeColor = palette.TextSecondary;
                tile.Icon.BackColor = palette.IsDark ? palette.SurfaceMuted : Color.FromArgb(244, 247, 250);
            }
        }
    }

    private void DisposeOwnedImages()
    {
        foreach (Image image in _ownedImages) image.Dispose();
        _ownedImages.Clear();
    }

    private sealed record AssetTile(int Id, Panel Root, PictureBox Icon, Label Name);

    private sealed record RouteCard(
        OpggBuildOption Option,
        Panel Root,
        Panel Indicator,
        Label Label,
        Label WinRate,
        Label Sample,
        Label ItemTitle,
        Label RuneTitle,
        IReadOnlyList<AssetTile> ItemTiles,
        IReadOnlyList<AssetTile> RuneTiles);
}
