using System.Globalization;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.GameData;

namespace LOL_GameAssistant.BaseViewForm;

public partial class RecordForm
{
    private readonly AntdUI.Label _equipmentLabel = new() { Text = "装备", AutoSize = false };
    private readonly AntdUI.Label _loadoutLabel = new() { Text = "符文", AutoSize = false };
    private readonly AntdUI.Label _loadoutStatus = new() { AutoSize = false };
    private readonly FlowLayoutPanel _selectedLoadout = new() { Name = "selectedLoadout", WrapContents = true };
    private readonly UiToolTip _loadoutTip = new();

    private void InitializeCompactLayout()
    {
        Controls.AddRange([_equipmentLabel, _loadoutLabel, _loadoutStatus, _selectedLoadout]);
        foreach (var label in new[] { _equipmentLabel, _loadoutLabel, _loadoutStatus, game_cs, game_damage, game_gold })
            label.Font = new Font(UiMetrics.FontFamily, 8.5F);
        game_name.Font = new Font(UiMetrics.FontFamily, 10F, FontStyle.Bold);
        game_msg.Font = new Font(UiMetrics.FontFamily, 12F, FontStyle.Bold);
        game_msg.TextAlign = ContentAlignment.MiddleRight;
        game_time.TextAlign = game_duration.TextAlign = ContentAlignment.MiddleRight;
        game_time.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        foreach (var label in new[] { game_type, game_name, game_cs, game_damage, game_gold })
        {
            label.AutoEllipsis = true;
            label.TextMultiLine = false;
        }
        _loadoutLabel.Name = "loadoutLabel";
        _loadoutStatus.Name = "loadoutStatus";
        Disposed += (_, _) => _loadoutTip.Dispose();
    }

    internal static string FormatCompactNumber(int value) => value >= 1000
        ? (value / 1000d).ToString("0.#", CultureInfo.InvariantCulture) + "k"
        : value.ToString(CultureInfo.InvariantCulture);

    private void BuildSelectedLoadout(MatchParticipant participant, bool augmented)
    {
        ControlLifetime.ClearAndDispose(_selectedLoadout);
        _loadoutLabel.Text = augmented ? "强化" : "符文";
        var ids = (augmented ? participant.stats?.AugmentIds : participant.stats?.RuneIds)?
            .Where(id => id > 0).ToArray() ?? [];
        _loadoutStatus.Text = augmented ? "未记录已选强化" : "未记录符文";
        _loadoutStatus.Visible = ids.Length == 0;
        foreach (int id in ids)
        {
            var tag = new AntdUI.Tag
            {
                Name = augmented ? "selectedAugment" : "selectedRune",
                Text = augmented ? $"强化 #{id}" : $"#{id}",
                AutoEllipsis = true,
                Font = new Font(UiMetrics.FontFamily, 8F),
                Tag = augmented,
                Margin = new Padding(0),
                AccessibleName = augmented ? $"已选强化 {id}" : $"已选符文 {id}"
            };
            tag.Image = null;
            tag.Disposed += (_, _) => tag.Image?.Dispose();
            tag.DoubleClick += FormOrControl_DoubleClick;
            _selectedLoadout.Controls.Add(tag);
            _ = PopulateLoadoutTagAsync(tag, id, augmented);
        }
        ApplyCompactTheme(UiTheme.Palette);
        LayoutRecord();
    }

    private async Task PopulateLoadoutTagAsync(AntdUI.Tag tag, int id, bool augmented)
    {
        try
        {
            Image? image;
            if (augmented)
            {
                var display = (await AugmentCatalog.ResolveAsync([id])).First();
                if (IsDisposed || tag.IsDisposed) return;
                tag.Text = UiLanguage.IsEnglish && !string.IsNullOrWhiteSpace(display.EnglishName)
                    ? display.EnglishName : display.Name;
                tag.AccessibleName = tag.Text;
                _loadoutTip.SetToolTip(tag, tag.Text + "\n\n" + AssetDetailToolTip.PlainText(AugmentCatalog.GetDescription(id)));
                byte[]? bytes = await AugmentCatalog.GetIconAsync(display.IconUrl, _lifetime.Token);
                if (bytes == null || tag.IsDisposed) return;
                using var stream = new MemoryStream(bytes);
                using var source = Image.FromStream(stream);
                image = new Bitmap(source);
            }
            else
            {
                string? details = await _gameAssetService.GetRuneDetailsAsync(id, _lifetime.Token);
                if (IsDisposed || tag.IsDisposed) return;
                _loadoutTip.SetToolTip(tag, AssetDetailToolTip.PlainText(details ?? $"符文 #{id}"));
                tag.AccessibleName = details ?? tag.AccessibleName;
                image = await LoadImageAsync(() => _gameAssetService.GetRuneIconAsync(id, _lifetime.Token));
                if (image != null) tag.Text = "";
            }
            if (IsDisposed || tag.IsDisposed) { image?.Dispose(); return; }
            tag.Image = image;
        }
        catch (OperationCanceledException) when (_lifetime.Token.IsCancellationRequested) { }
        catch (Exception error) { RuntimeDiagnostics.Report("首页符文强化", "图标暂不可用", error.Message); }
    }

    private int LayoutSelectedLoadout(int width)
    {
        int Scale(int value) => value * DeviceDpi / 96;
        int count = _selectedLoadout.Controls.Count;
        if (count == 0) return 1;
        bool augmented = _selectedLoadout.Controls[0].Tag is true;
        int columns = augmented ? Math.Max(1, width / Scale(88)) : Math.Max(1, width / Scale(30));
        columns = Math.Min(columns, count);
        int gap = Scale(4);
        int cellWidth = augmented ? Math.Max(1, width / columns - gap) : Scale(26);
        foreach (Control control in _selectedLoadout.Controls)
        {
            control.Size = new Size(cellWidth, Scale(24));
            control.Margin = new Padding(0, 0, gap, Scale(4));
        }
        return (count + columns - 1) / columns;
    }

    private void ApplyCompactTheme(ThemePalette palette)
    {
        _selectedLoadout.BackColor = Color.Transparent;
        foreach (AntdUI.Tag tag in _selectedLoadout.Controls)
        {
            tag.ColorScheme = palette.IsDark ? AntdUI.TAMode.Dark : AntdUI.TAMode.Light;
            tag.BackColor = palette.SurfaceMuted;
            tag.ForeColor = palette.TextPrimary;
        }
        game_dj.ColorScheme = palette.IsDark ? AntdUI.TAMode.Dark : AntdUI.TAMode.Light;
        game_dj.DefaultBack = palette.SurfaceMuted;
        game_dj.ForeColor = palette.TextPrimary;
        // 保留胜负色条；主体使用轻微着色，提高数据与小图标的可读性。
        Color tint = _isWin == false ? Color.FromArgb(239, 102, 129) : Color.FromArgb(81, 194, 142);
        Color surface = palette.SurfaceRaised;
        BackColor = Color.FromArgb((surface.R * 95 + tint.R * 5) / 100,
            (surface.G * 95 + tint.G * 5) / 100, (surface.B * 95 + tint.B * 5) / 100);
    }
}
