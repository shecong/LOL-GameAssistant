using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public sealed partial class SkinToolsForm
{
    private SkinPickerOverlayController? _skinPicker;

    private bool CanUseSkinPicker => !_disposing && _inProgress && _core.Enabled;

    private async Task ToggleSkinPickerAsync()
    {
        if (!CanUseSkinPicker) { _skinPicker?.Hide(); return; }
        if (_skinPicker?.OverlayVisible == true) { _skinPicker.Hide(); return; }
        if (!WindowHoldController.TryGetForegroundGameBounds(out var bounds)) return;
        if (_skinPicker == null)
        {
            _skinPicker = new SkinPickerOverlayController(() => CanUseSkinPicker,
                id => _ = SelectOverlaySkinAsync(id), () => _ = RefreshAsync(), () => _ = ChangeAsync(true));
            _status.TextChanged += (_, _) => UpdateSkinPicker();
        }
        UpdateSkinPicker();
        _skinPicker.ShowOverGame(bounds);
        if (_catalog == null) await RefreshAsync();
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
        var selected = _catalog?.Entries.FirstOrDefault(e => e.Model == _catalog.ActiveModel && e.SkinNum == _catalog.Skin && e.Gear == _catalog.Gear);
        _skinPicker.SetState(_catalog?.Entries.Select(e => (e.EntryId, GetSkinDisplayName(e))).ToArray() ?? [],
            selected?.EntryId, _status.Text ?? "", !_busy && !_refreshing && _catalog != null, !_busy && !_refreshing);
    }
}
