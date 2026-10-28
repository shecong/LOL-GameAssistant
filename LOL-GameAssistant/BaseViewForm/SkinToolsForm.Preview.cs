using Label = AntdUI.Label;
using Panel = AntdUI.Panel;
using PictureBox = AntdUI.Avatar;
using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Domain.Skins;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public sealed partial class SkinToolsForm
{
    private readonly FlowLayoutPanel _skinNavigation = new() { Name = "SkinNavigation", Dock = DockStyle.Top, Height = 64,
        Padding = new Padding(12, 8, 0, 8), WrapContents = false };
    private readonly AntdUI.Button _heroSkinTab = new() { Name = "HeroSkinTab", Text = "游戏换肤", Width = 156, Height = 48,
        Font = new Font(UiMetrics.FontFamily, 11F, FontStyle.Bold), Margin = new Padding(0, 0, 18, 0), Type = AntdUI.TTypeMini.Primary, BorderWidth = 1, Radius = 8 };
    private readonly AntdUI.Button _objectSkinTab = new() { Name = "ObjectSkinTab", Text = "小兵与眼位换肤", Width = 180, Height = 48,
        Font = new Font(UiMetrics.FontFamily, 11F, FontStyle.Bold), Margin = new Padding(0, 0, 18, 0), BorderWidth = 1, Radius = 8 };
    private readonly AntdUI.Button _otherSettingsTab = new() { Name = "OtherSkinSettingsTab", Text = "其他设置", Width = 140, Height = 48,
        Font = new Font(UiMetrics.FontFamily, 11F, FontStyle.Bold), Margin = Padding.Empty, BorderWidth = 1, Radius = 8 };
    private readonly IChampionSkinResourceService _skinResources;
    private readonly Dictionary<int, ChampionSkinResource> _clientSkins = [];
    private readonly PictureBox _poster = new() { Name = "SkinPoster", Dock = DockStyle.Fill, ImageFit = AntdUI.TFit.Contain };
    private readonly Label _posterCaption = new() { Dock = DockStyle.Bottom, Height = 60, Text = "连接游戏后显示当前英雄皮肤海报", TextAlign = ContentAlignment.MiddleCenter };
    private CancellationTokenSource? _previewCancellation;
    private string _previewKey = "";

    private void BuildSkinNavigation(Control heroPage)
    {
        var navigation = _skinNavigation;
        navigation.AutoScroll = true;
        navigation.Controls.AddRange([_heroSkinTab, _objectSkinTab, _rankBorderTab, _otherSettingsTab]);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 8, 12, 12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var operations = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var objects = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12), Visible = false };
        var objectCard = CreateObjectCard();
        objectCard.Dock = DockStyle.Top;
        objectCard.Height = 520;
        objects.Controls.Add(objectCard);
        var otherSettings = CreateOtherSettingsPage();
        var rankBorders = CreateRankBorderPage();
        operations.Controls.Add(heroPage);
        operations.Controls.Add(objects);
        operations.Controls.Add(otherSettings);
        operations.Controls.Add(rankBorders);
        var preview = new Panel { Name = "SkinPosterPanel", Dock = DockStyle.Fill, Padding = new Padding(12), Margin = Padding.Empty };
        preview.Controls.Add(_poster);
        preview.Controls.Add(_posterCaption);
        layout.Controls.Add(operations, 0, 0);
        layout.Controls.Add(preview, 1, 0);
        Controls.Add(layout);
        Controls.Add(navigation);
        void SelectPage(int index)
        {
            heroPage.Visible = index == 0;
            objects.Visible = index == 1;
            otherSettings.Visible = index == 2;
            rankBorders.Visible = index == 3;
            var tabs = new[] { _heroSkinTab, _objectSkinTab, _otherSettingsTab, _rankBorderTab };
            for (int i = 0; i < tabs.Length; i++)
            {
                tabs[i].Type = i == index ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default;
                tabs[i].ForeColor = i == index ? Color.White : ForeColor;
            }
        }
        _heroSkinTab.Click += (_, _) => SelectPage(0);
        _objectSkinTab.Click += (_, _) => SelectPage(1);
        _otherSettingsTab.Click += (_, _) => SelectPage(2);
        _rankBorderTab.Click += (_, _) => SelectPage(3);
        layout.SizeChanged += (_, _) =>
        {
            bool wide = layout.ClientSize.Width >= 900;
            preview.Visible = wide;
            layout.ColumnStyles[0].Width = wide ? 55 : 100;
            layout.ColumnStyles[1].Width = wide ? 45 : 0;
        };
        _pageSurfaces.AddRange([layout, operations, objects, otherSettings, preview]);
    }

    private string GetSkinDisplayName(SkinEntry entry)
    {
        if (!_clientSkins.TryGetValue(entry.SkinNum, out var skin)) return entry.Name + (string.IsNullOrWhiteSpace(entry.GearName) ? "" : $" · {entry.GearName}");
        string? variant = entry.GearName;
        if (string.IsNullOrWhiteSpace(variant) && !string.Equals(entry.Model, _catalog?.Model, StringComparison.OrdinalIgnoreCase)) variant = entry.Name;
        return skin.Name + (string.IsNullOrWhiteSpace(variant) || skin.Name.Contains(variant, StringComparison.OrdinalIgnoreCase) ? "" : $" · {variant}");
    }

    private SkinQuality GetSkinQuality(SkinEntry entry) => entry.SkinNum == 0 ? SkinQuality.Base
        : _clientSkins.GetValueOrDefault(entry.SkinNum)?.Quality ?? SkinQuality.Unknown;

    private string GetSkinQualityPrefix(SkinEntry entry)
    {
        string quality = SkinQualityStyle.Label(GetSkinQuality(entry));
        if (_clientSkins.GetValueOrDefault(entry.SkinNum)?.IsChroma == true) quality += " · " + UiLanguage.T("炫彩");
        return $"【{quality}】";
    }

    private string GetSkinOptionText(SkinEntry entry) => GetSkinQualityPrefix(entry) + GetSkinDisplayName(entry);

    private void UpdateSkinQualityColors(ThemePalette palette)
    {
        for (int i = 0; i < _entries.Items.Count && i < _filtered.Count; i++)
            if (_entries.Items[i] is AntdUI.SelectItem item)
            {
                item.Fore = SkinQualityStyle.Foreground(GetSkinQuality(_filtered[i]), palette);
                item.ForeSub = palette.TextPrimary;
            }
        _entries.ForeColor = palette.TextPrimary;
        var selected = _entries.SelectedIndex >= 0 && _entries.SelectedIndex < _filtered.Count ? _filtered[_entries.SelectedIndex] : null;
        _entries.PrefixText = selected == null ? null : GetSkinQualityPrefix(selected);
        _entries.PrefixFore = selected == null ? palette.TextPrimary : SkinQualityStyle.Foreground(GetSkinQuality(selected), palette);
        _entries.Text = selected == null ? "" : GetSkinDisplayName(selected);
        _entries.Invalidate();
    }

    private async Task UpdatePreviewAsync()
    {
        if (_disposing || !Visible || _catalog == null) return;
        var selected = _entries.SelectedIndex >= 0 && _entries.SelectedIndex < _filtered.Count
            ? _filtered[_entries.SelectedIndex] : null;
        int skin = selected?.SkinNum ?? _catalog.Skin;
        string model = _catalog.Model;
        var catalog = _catalog;
        string key = $"{model}:{skin}";
        if (_previewKey == key) return;
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = new CancellationTokenSource();
        var token = _previewCancellation.Token;
        _previewKey = key;
        _poster.Image?.Dispose();
        _poster.Image = null;
        _posterCaption.Text = $"{(selected == null ? _clientSkins.GetValueOrDefault(skin)?.Name ?? model : GetSkinDisplayName(selected))} · 正在加载海报…";
        try
        {
            var resources = await _skinResources.GetCatalogAsync(model, token);
            if (token.IsCancellationRequested || IsDisposed || _catalog?.Session != catalog.Session) return;
            _clientSkins.Clear();
            foreach (var resource in resources) _clientSkins[resource.SkinNumber] = resource;
            FilterEntries();
            var matching = _clientSkins.GetValueOrDefault(skin);
            var asset = matching == null ? null : await _skinResources.GetPosterAsync(matching, token);
            if (token.IsCancellationRequested || IsDisposed) return;
            if (asset is not { IsEmpty: false })
            {
                _previewKey = "";
                _posterCaption.Text = $"{(selected == null ? _clientSkins.GetValueOrDefault(skin)?.Name ?? model : GetSkinDisplayName(selected))} · 暂无此皮肤海报";
                return;
            }
            var bitmap = await Task.Run(() =>
            {
                using var stream = new MemoryStream(asset.Content);
                using var source = Image.FromStream(stream);
                return new Bitmap(source);
            }, token);
            if (token.IsCancellationRequested || IsDisposed) { bitmap.Dispose(); return; }
            _poster.Image = bitmap;
            _posterCaption.Text = $"{(selected == null ? _clientSkins.GetValueOrDefault(skin)?.Name ?? model : GetSkinDisplayName(selected))} · 皮肤海报";
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (!IsDisposed && !token.IsCancellationRequested)
            {
                _previewKey = "";
                _posterCaption.Text = "海报暂不可用，可重新读取英雄重试";
            }
        }
    }
}
