using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.GameData;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>当前对局的推荐列表；只有明确应用方案后才由调用方写入客户端。</summary>
internal sealed class OpggBuildPickerForm : AntdUI.Window, IThemeAware
{
    private static readonly Color Navy = Color.FromArgb(3, 23, 33);
    private static readonly Color SelectedNavy = Color.FromArgb(7, 35, 47);
    private static readonly Color Gold = Color.FromArgb(207, 174, 104);
    private static readonly Color Muted = Color.FromArgb(113, 151, 164);
    private static readonly Color Line = Color.FromArgb(51, 64, 62);
    private readonly OpggBuildChoices _choices;
    private readonly IOpggBuildApplyService? _buildService;
    private readonly bool _allowApply;
    private readonly Label _heading = new();
    private readonly Label _source = new();
    private readonly AntdUI.PageHeader _windowHeader = new()
    {
        Dock = DockStyle.Top, Height = 38, ShowButton = true, ShowIcon = true,
        UseSystemStyleColor = false, UseForeColorDrawIcons = true, MinimizeBox = false,
        ColorScheme = AntdUI.TAMode.Dark
    };
    private readonly List<Button> _laneTabs = new();
    private readonly Dictionary<(string Mode, string Position), OpggBuildChoices> _cache = new();
    private CancellationTokenSource? _queryCancellation;
    private int _queryVersion;
    private bool _loading;
    private string _position;
    private readonly string _initialPosition;
    private readonly int _initialOrder;
    private bool _hasLoadedCategory;
    private readonly IGameAssetService _gameAssetService;
    private readonly FlowLayoutPanel _routeCards = new();
    private readonly Button _apply = CreateGoldButton("应用选中方案");
    private readonly Label _note = new();
    private readonly Label _empty = new();
    private readonly PictureBox _championIcon = new();
    private readonly CheckBox _replaceCurrentRunePage = new()
    {
        Text = "符文页已满时，允许覆盖当前自定义符文页", Checked = true,
        AutoSize = true, ForeColor = Muted, BackColor = Navy
    };
    private readonly List<RouteCard> _cards = new();
    private readonly List<Button> _tabs = new();
    private readonly List<Image> _ownedImages = new();
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 7000, InitialDelay = 250, ReshowDelay = 100 };
    private readonly SemaphoreSlim _visualLoadGate = new(6, 6);
    private RouteCard? _selectedCard;
    private string _filter = "all";

    /// <summary>返回推荐选择器当前选中的方案。</summary>
    public OpggBuildOption? SelectedOption => _selectedCard?.Option;
    /// <summary>返回推荐选择器当前选中的分路。</summary>
    public string SelectedPosition => _selectedCard?.Position ?? _position;
    /// <summary>返回用户是否允许方案应用时覆盖当前符文页。</summary>
    public bool AllowReplaceCurrentRunePage => _replaceCurrentRunePage.Checked;

    /// <summary>初始化 OpggBuildPickerForm 的实例状态，并保存传入的依赖或数据。</summary>
    public OpggBuildPickerForm(OpggBuildChoices choices, IGameAssetService gameAssetService,
        int initiallySelectedOrder = 0, IOpggBuildApplyService? buildService = null, bool allowApply = true)
    {
        _choices = choices;
        _gameAssetService = gameAssetService;
        _buildService = buildService;
        _allowApply = allowApply;
        _position = choices.PositionName switch
        {
            "上路" => "TOP", "打野" => "JUNGLE", "下路" => "BOTTOM", "辅助" => "UTILITY", _ => "MIDDLE"
        };
        _initialPosition = _position;
        _initialOrder = initiallySelectedOrder;
        _cache[(choices.Mode, _position)] = choices;
        Text = $"一键配置 · {choices.ChampionName} · {choices.PositionName}";
        StartPosition = FormStartPosition.CenterScreen;
        AntdWindowChrome.Configure(this);
        Icon = AppIcon.Shared;
        _windowHeader.Text = Text;
        _windowHeader.Icon = AppIcon.Shared.ToBitmap();
        _ownedImages.Add(_windowHeader.Icon);
        BorderColor = Gold;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(800, 540);
        ClientSize = new Size(960, 700);
        Padding = new Padding(16, 0, 16, 0);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Navy;
        ForeColor = Gold;
        BuildUi(initiallySelectedOrder);
        UiTheme.Apply(this);
        Shown += async (_, _) =>
        {
            Activate();
            BringToFront();
            if (_buildService != null && _choices.Mode == "ranked")
                await ChangeCategoryAsync(_choices.Mode, "all");
            else await LoadVisualsAsync();
        };
    }

    /// <summary>在控件加载后完成依赖尺寸或句柄的初始化。</summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // 在父窗口所在屏幕的工作区居中，避开任务栏并支持多显示器。
        Rectangle area = Screen.FromControl(Owner ?? this).WorkingArea;
        Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2),
            area.Top + Math.Max(0, (area.Height - Height) / 2));
    }

    /// <summary>创建推荐方案中的文本标签。</summary>
    private static Label TextLabel(string text, Color color, int height = 24) => new()
    {
        Text = text, ForeColor = color, BackColor = Color.Transparent,
        AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Height = height,
        Dock = DockStyle.Fill, Margin = Padding.Empty
    };

    /// <summary>创建推荐选择器使用的金色操作按钮。</summary>
    private static Button CreateGoldButton(string text) => new()
    {
        Text = text, FlatStyle = FlatStyle.Flat, ForeColor = Gold, BackColor = Navy,
        Size = new Size(116, 32), Cursor = Cursors.Hand,
        FlatAppearance = { BorderColor = Gold, BorderSize = 1,
            MouseOverBackColor = Color.FromArgb(38, 51, 51), MouseDownBackColor = SelectedNavy }
    };

    /// <summary>创建装备、符文或召唤师技能图标行。</summary>
    private static FlowLayoutPanel AssetRow() => new()
    {
        Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true,
        Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Color.Transparent
    };

    /// <summary>创建当前界面使用的布局和操作控件。</summary>
    private void BuildUi(int initiallySelectedOrder)
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 154 };
        var tabs = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, WrapContents = false };
        var modes = new List<(string Key, string Title)> { ("all", "全部"), ("ranked", "峡谷"), ("aram", "大乱斗") };
        modes.Add(("aram_mayhem", "海克斯大乱斗"));
        modes.Add(("arena", "竞技场"));
        if (!modes.Any(mode => mode.Key == _choices.Mode)) modes.Add((_choices.Mode, _choices.PositionName));
        foreach (var mode in modes)
        {
            Button tab = CreateGoldButton(mode.Title);
            tab.Size = new Size(mode.Key == "aram_mayhem" ? 140 : 90, 40);
            tab.Margin = new Padding(0, 4, 10, 4);
            tab.Tag = mode.Key;
            tab.FlatAppearance.BorderSize = 0;
            tab.Click += async (_, _) =>
            {
                if (_buildService == null) FilterCards(mode.Key);
                else await ChangeCategoryAsync(mode.Key, _position);
            };
            _tabs.Add(tab);
            tabs.Controls.Add(tab);
        }
        var lanes = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, WrapContents = false };
        foreach (var lane in new[] { ("all", "全部分路"), ("TOP", "上路"), ("JUNGLE", "打野"),
            ("MIDDLE", "中路"), ("BOTTOM", "下路"), ("UTILITY", "辅助") })
        {
            var button = CreateGoldButton(lane.Item2);
            button.Tag = lane.Item1;
            button.Size = new Size(88, 30);
            button.Margin = new Padding(0, 4, 10, 4);
            button.Click += async (_, _) => await ChangeCategoryAsync(_filter, lane.Item1);
            _laneTabs.Add(button);
            lanes.Controls.Add(button);
        }
        var hero = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 8, 8, 10) };
        _championIcon.Dock = DockStyle.Left;
        _championIcon.Width = 42;
        _championIcon.SizeMode = PictureBoxSizeMode.Zoom;
        var heroText = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 0, 0) };
        var title = _heading;
        title.Text = $"{_choices.ChampionName}  /  {_choices.PositionName}  ·  {_choices.Options.Count} 套推荐";
        title.ForeColor = Gold;
        title.Height = 24;
        title.AutoEllipsis = true;
        title.Font = new Font(Font, FontStyle.Bold);
        title.Dock = DockStyle.Top;
        _note.Text = _choices.Mode == "aram_mayhem"
            ? "海克斯大乱斗专属出装与增幅推荐 · 此模式无常规符文页"
            : "选择一套方案，配置符文、装备与召唤师技能";
        if (!_allowApply) _note.Text = "未获取当前对局，默认展示推荐；进入选人阶段后可应用。";
        _note.Dock = DockStyle.Fill;
        _note.ForeColor = Muted;
        _note.TextAlign = ContentAlignment.MiddleLeft;
        _note.AutoEllipsis = true;
        heroText.Controls.Add(_note);
        heroText.Controls.Add(title);
        hero.Controls.Add(heroText);
        hero.Controls.Add(_championIcon);
        header.Controls.Add(hero);
        header.Controls.Add(lanes);
        header.Controls.Add(tabs);

        _routeCards.Dock = DockStyle.Fill;
        _routeCards.AutoScroll = true;
        _routeCards.FlowDirection = FlowDirection.TopDown;
        _routeCards.WrapContents = false;
        _routeCards.Padding = new Padding(0, 4, 0, 0);
        _routeCards.SizeChanged += (_, _) => ResizeRouteCards();
        foreach (OpggBuildOption option in _choices.Options)
        {
            RouteCard card = CreateRouteCard(option);
            _cards.Add(card);
            _routeCards.Controls.Add(card.Root);
        }
        _empty.Height = 100;
        _empty.TextAlign = ContentAlignment.MiddleCenter;
        _empty.ForeColor = Muted;
        _empty.Visible = false;
        _routeCards.Controls.Add(_empty);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 72, ColumnCount = 2, RowCount = 2,
            Padding = new Padding(8, 10, 0, 8)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _replaceCurrentRunePage.Visible = _choices.Options.Any(option => option.RunePerkIds.Count >= 6);
        footer.Controls.Add(_replaceCurrentRunePage, 0, 0);
        _source.Text = "数据来源：" + (_choices.Mode == "aram_mayhem" ? "aramgg / ARAMKit" : "OP.GG");
        _source.Dock = DockStyle.Fill;
        _source.ForeColor = Muted;
        footer.Controls.Add(_source, 0, 1);
        _apply.Anchor = AnchorStyles.Right;
        _apply.Enabled = false;
        _apply.Click += (_, _) => ConfirmSelection();
        footer.Controls.Add(_apply, 1, 0);
        footer.SetRowSpan(_apply, 2);
        Controls.Add(_routeCards);
        Controls.Add(footer);
        Controls.Add(header);
        Controls.Add(_windowHeader);
        AcceptButton = _apply;
        // Esc cancels without applying the currently highlighted route.
        var cancel = new Button { DialogResult = DialogResult.Cancel, Visible = false };
        Controls.Add(cancel);
        CancelButton = cancel;
        if (_cards.Count > 0)
            SelectCard(_cards.FirstOrDefault(card => card.Option.Order == initiallySelectedOrder) ?? _cards[0]);
        FilterCards(_choices.Mode);
    }

    /// <summary>创建一条推荐路线的卡片。</summary>
    private RouteCard CreateRouteCard(OpggBuildOption option, OpggBuildChoices? context = null, string? position = null)
    {
        var choices = context ?? _choices;
        bool mayhem = option.Mode == "aram_mayhem";
        var root = new Panel
        {
            Width = 900, Height = 212, Padding = new Padding(20, 14, 14, 14),
            Margin = new Padding(0, 0, 0, 4), Cursor = Cursors.Hand
        };
        root.Paint += (_, e) =>
        {
            using var pen = new Pen(Line);
            e.Graphics.DrawLine(pen, 0, root.Height - 1, root.Width, root.Height - 1);
            if (_selectedCard?.Root == root)
            {
                using var selection = new SolidBrush(Gold);
                e.Graphics.FillRectangle(selection, 0, 14, 2, root.Height - 28);
            }
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var heading = TextLabel($"方案 {option.Order:00}  ·  {choices.ChampionName} · " +
            (option.Order == 1 ? "热门出装" : "备选出装") + $"    [{choices.PositionName}]", Gold);
        heading.Font = new Font(Font, FontStyle.Bold);
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(TextLabel(mayhem ? "aramgg / ARAMKit" : "OP.GG 推荐", Gold), 1, 0);

        var details = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 2, Margin = Padding.Empty };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        details.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        details.Controls.Add(VerticalTag(mayhem ? "增\n幅" : "符\n文"), 0, 0);
        var runes = AssetRow();
        var runeTiles = new List<AssetTile>();
        if (mayhem)
        {
            var catalog = AugmentCatalog.GetAll().ToDictionary(item => item.Id);
            foreach (var augment in choices.Augments?.Take(3) ?? [])
            {
                catalog.TryGetValue(augment.Id, out var info);
                var text = TextLabel($"{info?.Name ?? $"#{augment.Id}"}  {augment.WinRate:0.0}%", Muted);
                text.Dock = DockStyle.None;
                text.Size = new Size(160, 40);
                _toolTip.SetToolTip(text, $"{text.Text} · {augment.Matches:N0} 场样本");
                runes.Controls.Add(text);
            }
            if (runes.Controls.Count == 0)
            {
                var noAugments = TextLabel("暂无增幅样本", Muted);
                noAugments.Dock = DockStyle.None;
                noAugments.Width = 200;
                runes.Controls.Add(noAugments);
            }
        }
        else
        {
            AddRune(runes, runeTiles, option.PrimaryStyleId, 34, 0);
            for (int i = 0; i < option.RunePerkIds.Count; i++)
            {
                if (i == 4) AddRune(runes, runeTiles, option.SubStyleId, 32, 14);
                AddRune(runes, runeTiles, option.RunePerkIds[i], i == 0 ? 34 : i >= 6 ? 20 : 26,
                    i == 6 ? 16 : 0);
            }
        }
        details.Controls.Add(runes, 1, 0);
        details.Controls.Add(VerticalTag("装\n备"), 0, 1);
        var equipment = AssetRow();
        var itemTiles = new List<AssetTile>();
        AddItemSection(equipment, "核心三件套", option.CoreItemIds, itemTiles);
        AddItemSection(equipment, "更多可选装", option.SituationalItemIds, itemTiles);
        details.Controls.Add(equipment, 1, 1);
        var extras = AssetRow();
        var spellTiles = new List<AssetTile>();
        if (option.StarterItemIds.Count > 0)
        {
            extras.Controls.Add(InlineTag("出门装"));
            foreach (int id in option.StarterItemIds.Where(id => id > 0))
            {
                var tile = CreateAssetTile(id, 24);
                itemTiles.Add(tile);
                extras.Controls.Add(tile.Root);
            }
        }
        if (option.SummonerSpellIds?.Count > 0)
        {
            extras.Controls.Add(InlineTag("召唤师技能"));
            foreach (int id in option.SummonerSpellIds.Where(id => id > 0))
            {
                var tile = CreateAssetTile(id, 24);
                spellTiles.Add(tile);
                extras.Controls.Add(tile.Root);
            }
        }
        details.Controls.Add(extras, 1, 2);
        layout.Controls.Add(details, 0, 1);
        var stats = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8, 24, 0, 0)
        };
        stats.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        stats.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        stats.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        stats.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var winRate = TextLabel(option.Matches > 0 || option.ReportedWinRate.HasValue
            ? $"胜率 {option.WinRate:0.#}%" : "暂无胜率统计", Color.FromArgb(232, 231, 209));
        winRate.TextAlign = ContentAlignment.MiddleRight;
        stats.Controls.Add(winRate, 0, 0);
        var sample = TextLabel(mayhem && option.PickRate.HasValue
            ? $"选择率 {option.PickRate:0.0}%" : $"{option.Matches:N0} 场样本", Muted);
        sample.TextAlign = ContentAlignment.MiddleRight;
        stats.Controls.Add(sample, 0, 1);
        var apply = CreateGoldButton("应用");
        apply.Name = $"applyRoute{option.Order}";
        apply.Anchor = AnchorStyles.Right;
        stats.Controls.Add(apply, 0, 2);
        layout.Controls.Add(stats, 1, 1);
        root.Controls.Add(layout);
        var card = new RouteCard(option, root, itemTiles, runeTiles, spellTiles, position ?? _position, apply);
        AttachSelectionHandler(root, card);
        apply.Click += (_, _) => { SelectCard(card); ConfirmSelection(); };
        return card;
    }

    /// <summary>创建竖向排列的路线标签。</summary>
    private static Label VerticalTag(string text) => new()
    {
        Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Color.FromArgb(156, 206, 220), BackColor = Color.FromArgb(9, 48, 63),
        Margin = new Padding(0, 4, 8, 4)
    };

    /// <summary>创建行内展示的说明标签。</summary>
    private static Label InlineTag(string text) => new()
    {
        Text = text, AutoSize = true, ForeColor = Muted,
        Margin = new Padding(8, 4, 8, 0)
    };

    /// <summary>向符文展示行追加一个图标条目。</summary>
    private void AddRune(FlowLayoutPanel row, List<AssetTile> tiles, int id, int size, int gap)
    {
        if (id <= 0) return;
        var tile = CreateAssetTile(id, size);
        tile.Root.Margin = new Padding(gap, (42 - size) / 2, 4, 0);
        tile.Icon.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Pen(id is >= 5000 and <= 5999 ? Muted : Gold);
            e.Graphics.DrawEllipse(pen, 0, 0, tile.Icon.Width - 1, tile.Icon.Height - 1);
        };
        tiles.Add(tile);
        row.Controls.Add(tile.Root);
    }

    /// <summary>向推荐卡片追加一个装备分组。</summary>
    private void AddItemSection(FlowLayoutPanel host, string title, IEnumerable<int> ids, List<AssetTile> tiles)
    {
        int[] items = ids.Where(id => id > 0).ToArray();
        if (items.Length == 0) return;
        var section = new Panel { Size = new Size(items.Length * 38, 62), Margin = new Padding(0, 0, 14, 0) };
        var caption = TextLabel(title, Muted, 22);
        caption.TextAlign = ContentAlignment.MiddleCenter;
        caption.BackColor = Color.FromArgb(5, 36, 48);
        caption.Tag = "equipmentCaption";
        caption.Dock = DockStyle.Top;
        var row = AssetRow();
        foreach (int id in items)
        {
            var tile = CreateAssetTile(id, 34);
            tile.Root.Margin = new Padding(0, 4, 4, 0);
            tiles.Add(tile);
            row.Controls.Add(tile.Root);
        }
        section.Controls.Add(row);
        section.Controls.Add(caption);
        host.Controls.Add(section);
    }

    /// <summary>创建图标和文字组成的资源条目。</summary>
    private AssetTile CreateAssetTile(int id, int size)
    {
        var root = new Panel { Size = new Size(size, size), Margin = new Padding(0, 0, 4, 0), Cursor = Cursors.Hand };
        var icon = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = SelectedNavy };
        var name = new Label { Text = $"#{id}", Visible = false };
        root.Controls.Add(name);
        root.Controls.Add(icon);
        _toolTip.SetToolTip(icon, $"资源 {id} · 正在加载");
        icon.Paint += (_, e) =>
        {
            if (icon.Image == null)
                TextRenderer.DrawText(e.Graphics, id switch
                {
                    5001 or 5010 or 5011 => "血",
                    5002 => "甲",
                    5003 => "抗",
                    5005 => "速",
                    5007 => "急",
                    5008 => "适",
                    5013 => "韧",
                    _ => "·"
                }, Font, icon.ClientRectangle, Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        return new AssetTile(id, root, icon, name);
    }

    /// <summary>绑定条目点击事件，使子控件点击也能选择所属方案。</summary>
    private void AttachSelectionHandler(Control control, RouteCard card)
    {
        control.Click += (_, _) => SelectCard(card);
        foreach (Control child in control.Controls) AttachSelectionHandler(child, card);
    }

    /// <summary>更新当前选中的推荐卡片及其视觉状态。</summary>
    private void SelectCard(RouteCard card)
    {
        _selectedCard = card;
        _apply.Enabled = CanApply(card);
        ApplyTheme(UiTheme.Palette);
    }

    /// <summary>判断当前推荐是否具备应用到客户端的必要数据。</summary>
    private bool CanApply(RouteCard card) => _allowApply && !_loading && card.Option.Mode == _choices.Mode;

    private static readonly (string Key, string Name)[] Lanes =
        [("TOP", "上路"), ("JUNGLE", "打野"), ("MIDDLE", "中路"), ("BOTTOM", "下路"), ("UTILITY", "辅助")];

    /// <summary>切换推荐类别并加载对应方案。</summary>
    internal async Task ChangeCategoryAsync(string mode, string position)
    {
        if (_buildService == null) return;
        _queryCancellation?.Cancel();
        _queryCancellation?.Dispose();
        _queryCancellation = new CancellationTokenSource();
        var token = _queryCancellation.Token;
        int version = ++_queryVersion;
        _filter = mode;
        _position = position;
        string queryMode = mode == "all" ? _choices.Mode : mode;
        bool ranked = queryMode == "ranked";
        var roles = ranked && position == "all" ? Lanes.Select(lane => lane.Key).ToArray()
            : new[] { ranked ? position : "MIDDLE" };
        _loading = true;
        _selectedCard = null;
        _apply.Enabled = false;
        ClearRouteCards();
        _empty.Visible = true;
        _empty.Text = "正在加载推荐方案…";
        _note.Text = "正在获取所选模式与分路的数据…";
        ApplyTheme(UiTheme.Palette);
        try
        {
            var pending = roles.OrderBy(role => role == _initialPosition ? 0 : 1).Select(async role =>
            {
                if (_cache.TryGetValue((queryMode, role), out var cached)) return (Role: role, Choices: cached);
                var request = queryMode switch
                {
                    "aram" => new OpggBuildRequest("ARAM"),
                    "aram_mayhem" => new OpggBuildRequest("KIWI"),
                    "arena" => new OpggBuildRequest("CHERRY"),
                    "urf" => new OpggBuildRequest("URF"),
                    "nexus_blitz" => new OpggBuildRequest("NEXUSBLITZ"),
                    _ => new OpggBuildRequest("CLASSIC")
                };
                try
                {
                    var choices = await _buildService.GetBuildChoicesAsync(ResolveChampionId(), role, request, token);
                    token.ThrowIfCancellationRequested();
                    if (choices.Succeeded) _cache[(queryMode, role)] = choices;
                    return (Role: role, Choices: choices);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch { return (Role: role, Choices: OpggBuildChoices.Failure("推荐加载失败，请重试。")); }
            }).ToList();
            var results = new List<(string Role, OpggBuildChoices Choices)>();
            if (_championIcon.Image == null) _ = LoadChampionVisualAsync();
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending).WaitAsync(token);
                pending.Remove(completed);
                var result = await completed;
                if (IsDisposed || version != _queryVersion) return;
                results.Add(result);
                _routeCards.SuspendLayout();
                try
                {
                    if (result.Choices.Succeeded)
                    {
                        foreach (var option in result.Choices.Options)
                        {
                            var card = CreateRouteCard(option, result.Choices, result.Role);
                            _cards.Add(card);
                            _routeCards.Controls.Add(card.Root);
                            _ = LoadCardVisualsAsync(card);
                        }
                    }
                    _empty.Visible = _cards.Count == 0;
                    _empty.Text = pending.Count > 0 ? "正在加载推荐方案…" :
                        string.Join("；", results.Select(item => item.Choices.Message).Distinct());
                    _heading.Text = $"{_choices.ChampionName} / " + (ranked ? position == "all" ? "全部分路" :
                        Lanes.First(lane => lane.Key == position).Name : result.Choices.Succeeded
                            ? result.Choices.PositionName : queryMode) + $" · {_cards.Count} 套推荐";
                    _note.Text = queryMode != _choices.Mode ? "当前浏览其他模式；请切回当前对局模式后应用方案。" :
                        queryMode == "aram_mayhem" ? "海克斯大乱斗专属出装与增幅推荐 · 此模式无常规符文页" :
                        "选择一套方案，配置符文、装备与召唤师技能";
                    if (!_allowApply) _note.Text = "未获取当前对局，默认展示推荐；进入选人阶段后可应用。";
                    if (pending.Count > 0) _note.Text += $" · 正在补充其他分路（{results.Count}/{roles.Length}）";
                    var missing = results.Where(item => !item.Choices.Succeeded).Select(item =>
                        Lanes.First(lane => lane.Key == item.Role).Name).ToArray();
                    if (ranked && missing.Length > 0 && _cards.Count > 0)
                        _note.Text += $" · {string.Join("、", missing)}暂无可用数据";
                    _source.Text = "数据来源：" + (queryMode == "aram_mayhem" ? "aramgg / ARAMKit" : "OP.GG");
                    _replaceCurrentRunePage.Visible = _cards.Any(card => card.Option.RunePerkIds.Count >= 6);
                    // 已到达的分路可立即应用，不等待最慢的网络请求。
                    _loading = _cards.Count == 0 && pending.Count > 0;
                    if (_selectedCard == null && _cards.Count > 0)
                    {
                        var preferred = !_hasLoadedCategory && queryMode == _choices.Mode
                            ? _cards.FirstOrDefault(card => card.Position == _initialPosition && card.Option.Order == _initialOrder)
                            : null;
                        SelectCard(preferred ?? _cards[0]);
                        _hasLoadedCategory = true;
                    }
                    ApplyTheme(UiTheme.Palette);
                }
                finally { _routeCards.ResumeLayout(true); }
                ResizeRouteCards();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (IsDisposed || version != _queryVersion) return;
            _loading = false;
            _empty.Text = "推荐加载失败，请重新选择分路或类型重试。";
            _note.Text = "暂未取得所选类型的数据";
        }
    }

    /// <summary>清空已有推荐卡片并释放其图像资源。</summary>
    private void ClearRouteCards()
    {
        foreach (var card in _cards)
        {
            foreach (var tile in card.ItemTiles.Concat(card.RuneTiles).Concat(card.SpellTiles))
            {
                if (tile.Icon.Image is Image image)
                {
                    _ownedImages.Remove(image);
                    tile.Icon.Image = null;
                    image.Dispose();
                }
            }
            _routeCards.Controls.Remove(card.Root);
            card.Root.Dispose();
        }
        _cards.Clear();
        _routeCards.AutoScrollPosition = Point.Empty;
    }

    /// <summary>按当前筛选条件更新推荐卡片的可见状态。</summary>
    private void FilterCards(string mode)
    {
        _filter = mode;
        _routeCards.SuspendLayout();
        foreach (RouteCard card in _cards) card.Root.Visible = mode == "all" || card.Option.Mode == mode;
        var visible = _cards.Where(card => mode == "all" || card.Option.Mode == mode).ToArray();
        _empty.Visible = visible.Length == 0;
        _empty.Text = _cards.Count == 0 ? (_choices.Message.Length > 0 ? _choices.Message : "暂无可用推荐方案")
            : "当前对局没有此模式的推荐方案，请切回当前模式或“全部”。";
        if (!visible.Contains(_selectedCard)) _selectedCard = visible.FirstOrDefault();
        _apply.Enabled = _selectedCard != null && CanApply(_selectedCard);
        _routeCards.AutoScrollPosition = Point.Empty;
        _routeCards.ResumeLayout(true);
        ApplyTheme(UiTheme.Palette);
        ResizeRouteCards();
    }

    /// <summary>根据可用宽度调整推荐卡片布局。</summary>
    private void ResizeRouteCards()
    {
        int width = Math.Max(300, _routeCards.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
        foreach (RouteCard card in _cards) card.Root.Width = width;
        _empty.Width = width;
    }
    /// <summary>启动推荐界面所需的图标和名称加载。</summary>
    private async Task LoadVisualsAsync()
    {
        var tasks = _cards.ToArray().Select(LoadCardVisualsAsync).ToList();
        tasks.Add(LoadChampionVisualAsync());
        await Task.WhenAll(tasks);
    }

    /// <summary>加载单张推荐卡片中的英雄及资源图标。</summary>
    private Task LoadCardVisualsAsync(RouteCard card) => Task.WhenAll(
        card.ItemTiles.Select(LoadItemVisualAsync)
            .Concat(card.RuneTiles.Select(LoadRuneVisualAsync))
            .Concat(card.SpellTiles.Select(LoadSpellVisualAsync)));
    /// <summary>读取推荐卡片中的英雄图像。</summary>
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

    /// <summary>读取装备条目的图标和名称。</summary>
    private async Task LoadItemVisualAsync(AssetTile tile)
    {
        try
        {
            await _visualLoadGate.WaitAsync();
            try
            {
                if (IsDisposed || tile.Root.IsDisposed) return;
                Task<GameAsset?> iconTask = _gameAssetService.GetItemIconAsync(tile.Id);
                Task<string?> nameTask = _gameAssetService.GetItemNameAsync(tile.Id);
                await Task.WhenAll(iconTask, nameTask);
                if (IsDisposed || tile.Root.IsDisposed) return;
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
            if (IsDisposed || tile.Root.IsDisposed) return;
            tile.Name.Text = $"装备 {tile.Id}";
            _toolTip.SetToolTip(tile.Root, tile.Name.Text);
        }
    }

    /// <summary>读取符文条目的展示图标。</summary>
    private async Task LoadRuneVisualAsync(AssetTile tile)
    {
        try
        {
            await _visualLoadGate.WaitAsync();
            try
            {
                if (IsDisposed || tile.Root.IsDisposed) return;
                var asset = await _gameAssetService.GetRuneIconAsync(tile.Id);
                if (IsDisposed || tile.Root.IsDisposed) return;
                AssignImage(tile.Icon, ToImage(asset));
                _toolTip.SetToolTip(tile.Root, $"符文 {tile.Id}");
                _toolTip.SetToolTip(tile.Icon, $"符文 {tile.Id}");
            }
            finally
            {
                _visualLoadGate.Release();
            }
        }
        catch
        {
            if (IsDisposed || tile.Root.IsDisposed) return;
            _toolTip.SetToolTip(tile.Root, $"符文 {tile.Id}");
        }
    }

    /// <summary>根据推荐上下文确定英雄标识。</summary>
    private int ResolveChampionId() => _choices.ChampionId > 0
        ? _choices.ChampionId
        : AppCompositionRoot.ChampionCatalog.FindIdByDisplayName(_choices.ChampionName) ?? 0;

    /// <summary>将新图像绑定到资源条目，并处理已释放控件。</summary>
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

    /// <summary>将资源二进制转换为独立图像对象。</summary>
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

    /// <summary>确认当前方案并将选择结果返回调用方。</summary>
    private void ConfirmSelection()
    {
        if (_selectedCard == null || !CanApply(_selectedCard)) return;
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>读取召唤师技能条目的图标和名称。</summary>
    private async Task LoadSpellVisualAsync(AssetTile tile)
    {
        try
        {
            await _visualLoadGate.WaitAsync();
            try
            {
                if (IsDisposed || tile.Root.IsDisposed) return;
                var icon = await _gameAssetService.GetSummonerSpellIconAsync(tile.Id);
                var name = await _gameAssetService.GetSummonerSpellNameAsync(tile.Id);
                if (IsDisposed || tile.Root.IsDisposed) return;
                _toolTip.SetToolTip(tile.Icon, name ?? $"召唤师技能 {tile.Id}");
                AssignImage(tile.Icon, ToImage(icon));
            }
            finally { _visualLoadGate.Release(); }
        }
        catch { /* 图标不可用不会影响方案应用。 */ }
    }
    /// <summary>将语义主题颜色应用到当前控件或窗口。</summary>
    public void ApplyTheme(ThemePalette palette)
    {
        // 此推荐弹窗采用独立的游戏客户端配色，保持参考图的深蓝与金色。
        PaintControls(this);
        _windowHeader.ForeColor = Gold;
        foreach (RouteCard card in _cards)
        {
            card.Root.BackColor = ReferenceEquals(card, _selectedCard) ? SelectedNavy : Navy;
            card.Root.Invalidate();
            card.Apply.Enabled = CanApply(card);
        }
        foreach (Button tab in _tabs)
        {
            bool active = Equals(tab.Tag, _filter);
            tab.BackColor = active ? Color.FromArgb(36, 57, 60) : Navy;
            tab.ForeColor = active ? Color.FromArgb(239, 221, 166) : Gold;
            tab.FlatAppearance.BorderSize = active ? 1 : 0;
        }
        foreach (Button tab in _laneTabs)
        {
            tab.Enabled = (_filter == "all" ? _choices.Mode : _filter) == "ranked";
            bool active = Equals(tab.Tag, _position);
            tab.BackColor = active ? Color.FromArgb(36, 57, 60) : Navy;
            tab.FlatAppearance.BorderSize = active ? 1 : 0;
        }
    }

    /// <summary>按推荐选择器的配色更新各控件。</summary>
    private static void PaintControls(Control control)
    {
        control.BackColor = control is PictureBox ? SelectedNavy :
            control is Label label && label.Text.Contains('\n') ? Color.FromArgb(9, 48, 63) : Navy;
        if (control is Button) control.ForeColor = Gold;
        else if (control is CheckBox) control.ForeColor = Muted;
        foreach (Control child in control.Controls) PaintControls(child);
        // Let standard labels inherit the selected row background.
        if (control is Label { Visible: true } && !control.Text.Contains('\n')) control.BackColor = Color.Transparent;
        if (Equals(control.Tag, "equipmentCaption")) control.BackColor = Color.FromArgb(5, 36, 48);
        if (control is TableLayoutPanel or FlowLayoutPanel) control.BackColor = Color.Transparent;
    }

    /// <summary>释放当前对象持有的资源，结束相关事件订阅或后台任务。</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _queryCancellation?.Cancel();
            _queryCancellation?.Dispose();
            _queryCancellation = null;
            DisposeOwnedImages();
            _toolTip.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>释放当前界面自行持有的图像对象。</summary>
    private void DisposeOwnedImages()
    {
        foreach (Image image in _ownedImages) image.Dispose();
        _ownedImages.Clear();
    }

    /// <summary>推荐方案中的资源图标条目及其标识。</summary>
    private sealed record AssetTile(int Id, Panel Root, PictureBox Icon, Label Name);
    /// <summary>推荐路线卡片及其符文、装备和技能条目集合。</summary>
    private sealed record RouteCard(OpggBuildOption Option, Panel Root,
        IReadOnlyList<AssetTile> ItemTiles, IReadOnlyList<AssetTile> RuneTiles,
        IReadOnlyList<AssetTile> SpellTiles, string Position, Button Apply);
}
