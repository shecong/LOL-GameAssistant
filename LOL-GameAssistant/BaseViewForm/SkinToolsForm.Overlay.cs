using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Domain.Skins;

namespace LOL_GameAssistant.BaseViewForm;

public sealed partial class SkinToolsForm
{
    private SkinPickerOverlayController? _skinPicker;
    private SkinPickerCategory _skinPickerCategory;
    private int? _overlaySkinGroup;
    private string? _overlaySelectedEntry;

    private bool CanUseSkinPicker => !_disposing && _inProgress && _core.Enabled;

    private async Task ToggleSkinPickerAsync()
    {
        if (!CanUseSkinPicker) { _skinPicker?.Hide(); return; }
        if (_skinPicker?.OverlayVisible == true) { _skinPicker.Hide(); return; }
        if (!WindowHoldController.TryGetForegroundGameBounds(out var bounds)) return;
        if (_skinPicker == null)
        {
            _skinPicker = new SkinPickerOverlayController(() => CanUseSkinPicker,
                id => _ = SelectOverlayEntryAsync(id), () => _ = RefreshOverlayCategoryAsync(), () => _ = RestoreOverlayCategoryAsync(),
                changeCategory: category => _ = ChangeOverlayCategoryAsync(category), changeCamera: enabled => _ = SetCameraEnabledAsync(enabled));
            _status.TextChanged += (_, _) => UpdateSkinPicker();
            _objectStatus.TextChanged += (_, _) => UpdateSkinPicker();
        }
        FocusCurrentOverlaySkin();
        UpdateSkinPicker(force: true);
        _skinPicker.ShowOverGame(bounds);
        await RefreshOverlayCategoryAsync();
    }

    internal async Task ChangeOverlayCategoryAsync(SkinPickerCategory category)
    {
        if (!CanUseSkinPicker || _busy || _refreshing || !Enum.IsDefined(category)) return;
        _skinPickerCategory = category; _overlaySkinGroup = null; UpdateSkinPicker();
        if (category == SkinPickerCategory.Hero ? _catalog == null : _objectCatalog == null) await RefreshOverlayCategoryAsync();
    }

    internal Task RefreshOverlayCategoryAsync() => _skinPickerCategory == SkinPickerCategory.Hero ? RefreshAsync() : RunObjectsAsync(0);
    internal Task RestoreOverlayCategoryAsync() => _skinPickerCategory == SkinPickerCategory.Hero ? ChangeAsync(true) : RunObjectsAsync(2);

    internal async Task SelectOverlayEntryAsync(string entryId)
    {
        if (_skinPickerCategory == SkinPickerCategory.Hero)
        {
            if (!CanUseSkinPicker || _busy || _refreshing || _catalog == null) return;
            if (entryId == "group:back") { _overlaySkinGroup = null; UpdateSkinPicker(); return; }
            if (entryId.StartsWith("group:", StringComparison.Ordinal))
            {
                if (int.TryParse(entryId.AsSpan(6), out int group) && SkinGroupEntries(group).Count > 1)
                { _overlaySkinGroup = group; UpdateSkinPicker(); }
                return;
            }
            await SelectOverlaySkinAsync(entryId); return;
        }
        if (!CanUseSkinPicker || _busy || _refreshing || _objectCatalog == null || !int.TryParse(entryId, out int selected)) return;
        if (_skinPickerCategory == SkinPickerCategory.Minion)
        {
            if (selected < 0 || selected >= MinionSkinNames.Length) return;
            _minionSkins.SelectedIndex = selected + 1;
            await RunObjectSelectionAsync(1, selected, _appliedWardSkin);
        }
        else
        {
            int index = _objectCatalog.WardEntries.ToList().FindIndex(entry => entry.SkinNum == selected);
            if (index < 0) return;
            _wardSkins.SelectedIndex = index + 1;
            await RunObjectSelectionAsync(1, _appliedMinionIndex, selected);
        }
    }

    internal async Task SelectOverlaySkinAsync(string entryId)
    {
        if (!CanUseSkinPicker || _busy || _refreshing || _catalog == null) return;
        var entry = _catalog.Entries.FirstOrDefault(e => e.EntryId == entryId);
        if (entry == null) return;
        SelectSkinInControls(entry, clearSearch: true);
        await ChangeAsync(false);
    }

    private void FocusCurrentOverlaySkin()
    {
        var current = CurrentSkinEntry();
        _overlaySelectedEntry = current?.EntryId;
        _overlaySkinGroup = current != null && SkinGroupEntries(SkinGroupNumber(current)).Count > 1 ? SkinGroupNumber(current) : null;
    }

    private void UpdateSkinPicker(bool force = false)
    {
        if (_skinPicker == null || _skinPicker.IsDisposed) return;
        if (!CanUseSkinPicker) { _skinPicker.Hide(); return; }
        if (!force && !_skinPicker.OverlayVisible) return;
        _skinPicker.SetHotkey(_settings.Load().SkinPickerHotkey);
        _skinPicker.SetCameraState(_cameraEnabled.Checked, !_cameraBusy && !_busy && !_refreshing);
        bool available = !_busy && !_refreshing && !_objectsBusy;
        if (_skinPickerCategory != SkinPickerCategory.Hero)
        {
            _skinPicker.SetSubmenu([], null, null, "");
            var items = _skinPickerCategory == SkinPickerCategory.Ward
                ? _objectCatalog?.WardEntries.Select(e => (e.SkinNum.ToString(), $"{e.Name} · #{e.SkinNum}")).ToArray() ?? []
                : MinionSkinNames.Select((name, index) => (index.ToString(), name)).ToArray();
            int selectedId = _skinPickerCategory == SkinPickerCategory.Ward ? _appliedWardSkin : _appliedMinionIndex;
            string status = _objectCatalog == null ? "点击重新读取，获取当前小兵与自己的眼位。" : (_objectStatus.Text ?? "");
            _skinPicker.SetState(items, selectedId < 0 ? null : selectedId.ToString(), status,
                available && _objectCatalog != null, available, _skinPickerCategory, _objectCatalog?.Session ?? "");
            return;
        }
        var selected = CurrentSkinEntry();
        if (_overlaySelectedEntry != selected?.EntryId) FocusCurrentOverlaySkin();
        var itemsHero = new List<(string Id, string Name)>();
        var stylesHero = new List<LOL_GameAssistant.Application.GameData.GameOverlayItemStyle>();
        var palette = UiTheme.Palette with { IsDark = true };
        string? selectedIdHero = null;
            foreach (var entry in SortSkinEntries(SkinGroupRepresentatives()))
            {
                int groupNumberRoot = SkinGroupNumber(entry);
                int variants = SkinGroupEntries(groupNumberRoot).Count;
                string id = variants > 1 ? $"group:{groupNumberRoot}" : entry.EntryId;
                itemsHero.Add((id, GetSkinOptionText(entry) + (variants > 1 ? $" · 炫彩 / 形态 ({variants}) ›" : "")));
                stylesHero.Add(new(GetSkinQualityPrefix(entry), SkinQualityStyle.Foreground(GetSkinQuality(entry), palette)));
                if (selected != null && SkinGroupNumber(selected) == groupNumberRoot) selectedIdHero = id;
            }
        _skinPicker.SetState(itemsHero.ToArray(), selectedIdHero, _status.Text ?? "", available && _catalog != null, available,
            _skinPickerCategory, _catalog?.Session ?? "", stylesHero.ToArray());
        var children = _overlaySkinGroup is int groupNumber ? SkinGroupEntries(groupNumber) : [];
        _skinPicker.SetSubmenu(children.Count > 1 ? children.Select(e => (e.EntryId, GetSkinVariantListName(e))).ToArray() : [],
            selected?.EntryId, _overlaySkinGroup is int parent ? $"group:{parent}" : null,
            children.Count > 0 ? GetSkinDisplayName(children[0]) + " · 炫彩 / 形态" : "");
    }
}
