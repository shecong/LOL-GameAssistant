using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Domain.Skins;

namespace LOL_GameAssistant.BaseViewForm;

public sealed partial class SkinToolsForm
{
    private SkinPickerOverlayController? _skinPicker;
    private SkinPickerCategory _skinPickerCategory;

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
        UpdateSkinPicker();
        _skinPicker.ShowOverGame(bounds);
        if (_skinPickerCategory == SkinPickerCategory.Hero ? _catalog == null : _objectCatalog == null) await RefreshOverlayCategoryAsync();
    }

    internal async Task ChangeOverlayCategoryAsync(SkinPickerCategory category)
    {
        if (!CanUseSkinPicker || _busy || _refreshing || !Enum.IsDefined(category)) return;
        _skinPickerCategory = category; UpdateSkinPicker();
        if (category == SkinPickerCategory.Hero ? _catalog == null : _objectCatalog == null) await RefreshOverlayCategoryAsync();
    }

    internal Task RefreshOverlayCategoryAsync() => _skinPickerCategory == SkinPickerCategory.Hero ? RefreshAsync() : RunObjectsAsync(0);
    internal Task RestoreOverlayCategoryAsync() => _skinPickerCategory == SkinPickerCategory.Hero ? ChangeAsync(true) : RunObjectsAsync(2);

    internal async Task SelectOverlayEntryAsync(string entryId)
    {
        if (_skinPickerCategory == SkinPickerCategory.Hero) { await SelectOverlaySkinAsync(entryId); return; }
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
        _updatingSelection = true;
        try
        {
            _search.Text = "";
            FilterEntries();
            _entries.SelectedIndex = _filtered.FindIndex(e => e.EntryId == entryId);
        }
        finally { _updatingSelection = false; }
        await ChangeAsync(false);
    }

    private void UpdateSkinPicker()
    {
        if (_skinPicker == null || _skinPicker.IsDisposed) return;
        if (!CanUseSkinPicker) { _skinPicker.Hide(); return; }
        _skinPicker.SetHotkey(_settings.Load().SkinPickerHotkey);
        _skinPicker.SetCameraState(_cameraEnabled.Checked, !_cameraBusy && !_busy && !_refreshing);
        bool available = !_busy && !_refreshing && !_objectsBusy;
        if (_skinPickerCategory != SkinPickerCategory.Hero)
        {
            var items = _skinPickerCategory == SkinPickerCategory.Ward
                ? _objectCatalog?.WardEntries.Select(e => (e.SkinNum.ToString(), $"{e.Name} · #{e.SkinNum}")).ToArray() ?? []
                : MinionSkinNames.Select((name, index) => (index.ToString(), name)).ToArray();
            int selectedId = _skinPickerCategory == SkinPickerCategory.Ward ? _appliedWardSkin : _appliedMinionIndex;
            string status = _objectCatalog == null ? "点击重新读取，获取当前小兵与自己的眼位。" : (_objectStatus.Text ?? "");
            _skinPicker.SetState(items, selectedId < 0 ? null : selectedId.ToString(), status,
                available && _objectCatalog != null, available, _skinPickerCategory, _objectCatalog?.Session ?? "");
            return;
        }
        var selected = _catalog?.Entries.FirstOrDefault(e => e.Model == _catalog.ActiveModel && e.SkinNum == _catalog.Skin && e.Gear == _catalog.Gear);
        _skinPicker.SetState(_catalog?.Entries.Select(e => (e.EntryId, GetSkinOptionText(e))).ToArray() ?? [],
            selected?.EntryId, _status.Text ?? "", available && _catalog != null, available, _skinPickerCategory, _catalog?.Session ?? "",
            _catalog?.Entries.Select(e => new LOL_GameAssistant.Application.GameData.GameOverlayItemStyle(GetSkinQualityPrefix(e),
                SkinQualityStyle.Foreground(GetSkinQuality(e), UiTheme.Palette with { IsDark = true }))).ToArray());
    }
}
