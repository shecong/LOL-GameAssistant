using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>
/// 从本机客户端资源目录分页浏览生涯背景或召唤师头像。
/// 只在用户主动打开时读取，缩略图按当前可见页并发加载，避免一次占用大量 LCU 请求。
/// </summary>
internal sealed class ProfileAppearancePickerForm : AntdUI.Window, IThemeAware
{
    private const int PageSize = 36;
    private readonly IClientFeatureService _features;
    private readonly IProfileIconService _profileIcons;
    private readonly bool _isBackgroundPicker;
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 12000, InitialDelay = 300, ReshowDelay = 100 };
    private readonly SemaphoreSlim _imageGate = new(4, 4);
    private readonly AntdUI.Input _search = new() { Dock = DockStyle.Left, Width = 310, Height = 34, PlaceholderText = "搜索名称或 ID" };
    private readonly AntdUI.Label _summary = new() { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 8, 0) };
    private readonly AntdUI.Button _loadMore = new() { Dock = DockStyle.Bottom, Height = 38, Text = "加载更多" };

    private readonly FlowLayoutPanel _grid = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        Padding = new Padding(14),
        WrapContents = true
    };

    private readonly List<PickerItem> _items = [];
    private readonly List<Image> _tileImages = [];
    private int _visibleCount = PageSize;
    private int _renderVersion;

    public long SelectedId { get; private set; }

    public ProfileAppearancePickerForm(
        IClientFeatureService features,
        IProfileIconService profileIcons,
        bool isBackgroundPicker)
    {
        _features = features;
        _profileIcons = profileIcons;
        _isBackgroundPicker = isBackgroundPicker;
        Text = isBackgroundPicker ? "选择生涯背景" : "选择召唤师头像";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 560);
        Size = new Size(1080, 720);

        var toolbar = new AntdUI.Panel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(14, 9, 14, 9), Radius = 0 };
        toolbar.Controls.Add(_summary);
        toolbar.Controls.Add(_search);
        Controls.Add(_grid);
        Controls.Add(_loadMore);
        Controls.Add(toolbar);

        _search.TextChanged += (_, _) => { _visibleCount = PageSize; RenderTiles(); };
        _loadMore.Click += (_, _) => { _visibleCount += PageSize; RenderTiles(); };
        _toolTip.SetToolTip(_search, "可按英雄名、皮肤名或数字 ID 筛选。选择后会回填到客户端工具页，不会立即写入客户端。\n");
        _toolTip.SetToolTip(_loadMore, "继续加载下一页缩略图，避免首次打开时请求全部图片。\n");
        Load += async (_, _) => await LoadItemsAsync();
        Shown += (_, _) => UiTheme.Apply(this);
        Disposed += (_, _) =>
        {
            ++_renderVersion;
            DisposeTileImages();
            _toolTip.Dispose();
        };
    }

    public void ApplyTheme(ThemePalette palette)
    {
        BackColor = palette.Surface;
        ForeColor = palette.TextPrimary;
        _grid.BackColor = palette.Surface;
        _summary.BackColor = palette.SurfaceRaised;
        _summary.ForeColor = palette.TextSecondary;
    }

    private async Task LoadItemsAsync()
    {
        _summary.Text = "正在读取本机客户端资源…";
        _search.Enabled = false;
        try
        {
            if (_isBackgroundPicker)
            {
                IReadOnlyList<ClientSkinChoice> choices = await _features.GetProfileBackgroundChoicesAsync();
                _items.AddRange(choices.Select(choice => new PickerItem(
                    choice.SkinId,
                    choice.Name,
                    choice.ChampionName,
                    choice.IsOwned ? "已拥有 · 点击选择" : "客户端可识别 · 点击选择")));
            }
            else
            {
                IReadOnlyList<ProfileIconChoice> choices = await _profileIcons.GetProfileIconsAsync();
                _items.AddRange(choices.Select(choice => new PickerItem(
                    choice.IconId,
                    $"头像 {choice.IconId}",
                    "召唤师头像",
                    "点击选择")));
            }
            _summary.Text = _items.Count == 0
                ? "未读取到客户端资源，请确认英雄联盟客户端已启动并登录。"
                : $"共读取 {_items.Count} 项；先显示 {Math.Min(PageSize, _items.Count)} 项缩略图。";
            RenderTiles();
        }
        catch (Exception ex)
        {
            _summary.Text = $"读取失败：{ex.Message}";
        }
        finally
        {
            _search.Enabled = true;
        }
    }

    private void RenderTiles()
    {
        if (IsDisposed) return;
        int version = ++_renderVersion;
        DisposeTileImages();
        LOL_GameAssistant.Helper.ControlLifetime.ClearAndDispose(_grid);

        string query = _search.Text.Trim();
        PickerItem[] filtered = _items
            .Where(item => string.IsNullOrWhiteSpace(query)
                || item.Id.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || item.Group.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .ToArray();

        foreach (PickerItem item in filtered.Take(_visibleCount))
        {
            PictureBox picture;
            Control tile = CreateTile(item, out picture);
            _grid.Controls.Add(tile);
            _ = LoadTileImageAsync(item, picture, version);
        }

        _loadMore.Visible = filtered.Length > _visibleCount;
        if (_items.Count > 0)
            _summary.Text = $"显示 {Math.Min(filtered.Length, _visibleCount)} / {filtered.Length} 项；点击“选择”仅回填 ID，确认后再应用。";
    }

    private Control CreateTile(PickerItem item, out PictureBox picture)
    {
        int width = _isBackgroundPicker ? 190 : 118;
        int imageHeight = _isBackgroundPicker ? 102 : 72;
        ThemePalette palette = UiTheme.Palette;
        var tile = new AntdUI.Panel
        {
            Width = width,
            Height = _isBackgroundPicker ? 202 : 142,
            Padding = new Padding(7),
            Radius = 8,
            BorderWidth = 1,
            Margin = new Padding(6),
            BackColor = palette.SurfaceRaised,
            ForeColor = palette.TextPrimary
        };
        picture = new PictureBox
        {
            Dock = DockStyle.Top,
            Height = imageHeight,
            BackColor = Color.FromArgb(36, 46, 60),
            SizeMode = PictureBoxSizeMode.Zoom
        };
        var select = new AntdUI.Button { Dock = DockStyle.Bottom, Height = 28, Text = "选择" };
        select.Click += (_, _) => SelectItem(item);
        var detail = new AntdUI.Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Padding = new Padding(2, 4, 2, 0),
            Text = _isBackgroundPicker
                ? $"{item.Group}\n{item.Name}\nID {item.Id}"
                : $"ID {item.Id}",
            BackColor = palette.SurfaceRaised,
            ForeColor = palette.TextPrimary
        };
        string tip = _isBackgroundPicker
            ? $"{item.Group} · {item.Name}\n皮肤 ID：{item.Id}\n{item.Description}"
            : $"召唤师头像 ID：{item.Id}\n点击“选择”会回填 ID；需在上一页点击“应用头像”才写入客户端。";
        _toolTip.SetToolTip(tile, tip);
        _toolTip.SetToolTip(picture, tip);
        _toolTip.SetToolTip(select, tip);
        tile.Controls.Add(detail);
        tile.Controls.Add(select);
        tile.Controls.Add(picture);
        return tile;
    }

    private async Task LoadTileImageAsync(PickerItem item, PictureBox picture, int version)
    {
        try
        {
            await _imageGate.WaitAsync();
            byte[]? bytes = _isBackgroundPicker
                ? (await _features.GetProfileSkinPreviewAsync(item.Id))?.ImageBytes
                : await _profileIcons.GetProfileIconAsync((int)item.Id);
            if (bytes is not { Length: > 0 }) return;
            Image? image = DecodeImage(bytes);
            if (image == null) return;
            if (IsDisposed || picture.IsDisposed || version != _renderVersion)
            {
                image.Dispose();
                return;
            }
            picture.Image = image;
            _tileImages.Add(image);
        }
        catch
        {
            // 单张资源缺失不影响其它选择项。
        }
        finally
        {
            _imageGate.Release();
        }
    }

    private void SelectItem(PickerItem item)
    {
        SelectedId = item.Id;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void DisposeTileImages()
    {
        foreach (Image image in _tileImages) image.Dispose();
        _tileImages.Clear();
    }

    private static Image? DecodeImage(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var source = Image.FromStream(stream);
        return new Bitmap(source);
    }

    private sealed record PickerItem(long Id, string Name, string Group, string Description);
}