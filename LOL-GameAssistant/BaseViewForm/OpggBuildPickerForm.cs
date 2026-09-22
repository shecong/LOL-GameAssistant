using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Helper;

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
    private readonly PictureBox _championIcon = new();
    private readonly List<RouteCard> _cards = new();
    private readonly List<Image> _ownedImages = new();
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 7000, InitialDelay = 250, ReshowDelay = 100 };
    private readonly SemaphoreSlim _visualLoadGate = new(6, 6);
    private RouteCard? _selectedCard;

    public OpggBuildOption? SelectedOption => _selectedCard?.Option;

    public OpggBuildPickerForm(OpggBuildChoices choices, IGameAssetService gameAssetService)
    {
        _choices = choices;
        _gameAssetService = gameAssetService;
        Text = $"OP.GG 方案选择 · {choices.ChampionName} {choices.PositionName}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        MinimumSize = new Size(880, 590);
        ClientSize = new Size(1040, 700);
        Padding = new Padding(14);
        Font = new Font("Microsoft YaHei UI", 9F);

        BuildUi();
        UiTheme.Apply(this);
        Shown += async (_, _) => await LoadVisualsAsync();
        FormClosed += (_, _) => DisposeOwnedImages();
    }

    private void BuildUi()
    {
        var heroHeader = new Panel { Dock = DockStyle.Top, Height = 78, Padding = new Padding(10, 8, 10, 8) };
        _championIcon.Size = new Size(58, 58);
        _championIcon.SizeMode = PictureBoxSizeMode.Zoom;
        _championIcon.Dock = DockStyle.Left;

        var headerText = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 1, 0, 0) };
        _title.Dock = DockStyle.Top;
        _title.Height = 28;
        _title.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
        _title.Text = $"{_choices.ChampionName} · {_choices.PositionName} · 选择一套对局方案";
        _note.Dock = DockStyle.Fill;
        _note.Font = new Font("Microsoft YaHei UI", 9F);
        _note.Text = "每张卡片同时展示核心装备、符文图标、胜率与样本量。选择后点击“应用”，才会写入客户端。";
        headerText.Controls.Add(_note);
        headerText.Controls.Add(_title);
        heroHeader.Controls.Add(headerText);
        heroHeader.Controls.Add(_championIcon);

        _routeCards.Dock = DockStyle.Fill;
        _routeCards.AutoScroll = true;
        _routeCards.FlowDirection = FlowDirection.TopDown;
        _routeCards.WrapContents = false;
        _routeCards.Padding = new Padding(0, 6, 8, 4);
        _routeCards.SizeChanged += (_, _) => ResizeRouteCards();

        foreach (OpggBuildOption option in _choices.Options)
        {
            RouteCard card = CreateRouteCard(option);
            _cards.Add(card);
            _routeCards.Controls.Add(card.Root);
        }
        if (_cards.Count > 0) SelectCard(_cards[0]);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 9, 0, 0),
            WrapContents = false
        };
        var cancel = new Button { Text = "暂不应用", AutoSize = true, DialogResult = DialogResult.Cancel };
        _apply.Click += (_, _) => ConfirmSelection();
        footer.Controls.Add(cancel);
        footer.Controls.Add(_apply);

        Controls.Add(_routeCards);
        Controls.Add(footer);
        Controls.Add(heroHeader);
        AcceptButton = _apply;
        CancelButton = cancel;
    }

    private RouteCard CreateRouteCard(OpggBuildOption option)
    {
        var root = new Panel
        {
            Height = 214,
            Width = 1000,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(12),
            Cursor = Cursors.Hand
        };
        var indicator = new Panel { Dock = DockStyle.Left, Width = 5, Margin = new Padding(0, 0, 10, 0) };
        var content = new Panel { Dock = DockStyle.Fill };

        var cardHeader = new Panel { Dock = DockStyle.Top, Height = 36 };
        var label = new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
            Text = $"方案 {option.Order}"
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
        var itemTitle = new Label { Dock = DockStyle.Top, Height = 24, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold), Text = "推荐出装 · 起始 / 核心 / 备选" };
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
            Text = "符文 · 主系 / 副系 / 属性碎片"
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
        int width = Math.Max(720, _routeCards.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 10);
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

    private int ResolveChampionId() =>
        AppCompositionRoot.ChampionCatalog.FindIdByDisplayName(_choices.ChampionName) ?? 0;

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
