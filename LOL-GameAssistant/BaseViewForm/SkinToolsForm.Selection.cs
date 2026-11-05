using LOL_GameAssistant.Domain.Skins;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public sealed partial class SkinToolsForm
{
    private readonly AntdUI.Select _skinVariants = new() { Name = "HeroSkinVariants", Dock = DockStyle.Top, Height = 48,
        List = true, MaxCount = 12, AutoText = false, PrefixText = "炫彩 / 形态", PlaceholderText = "当前皮肤没有炫彩或其他形态" };
    private List<SkinEntry> _variants = [];
    private SkinEntry? SelectedSkinEntry => _skinVariants.SelectedIndex >= 0 && _skinVariants.SelectedIndex < _variants.Count
        ? _variants[_skinVariants.SelectedIndex]
        : _entries.SelectedIndex >= 0 && _entries.SelectedIndex < _filtered.Count ? _filtered[_entries.SelectedIndex] : null;

    private int SkinGroupNumber(SkinEntry entry) => _clientSkins.GetValueOrDefault(entry.SkinNum)?.ParentSkinNumber ?? entry.SkinNum;

    private List<SkinEntry> SkinGroupEntries(int group) => (_catalog?.Entries ?? []).Where(e => SkinGroupNumber(e) == group)
        .OrderBy(e => e.SkinNum != group).ThenBy(e => e.Gear >= 0).ToList();

    private List<SkinEntry> SkinGroupRepresentatives() => (_catalog?.Entries ?? []).GroupBy(SkinGroupNumber)
        .Select(group => group.OrderBy(e => e.SkinNum != group.Key).ThenBy(e => e.Gear >= 0).First()).ToList();

    private string GetSkinVariantDisplayName(SkinEntry entry)
    {
        int group = SkinGroupNumber(entry);
        var parent = SkinGroupEntries(group).FirstOrDefault();
        string parentName = _clientSkins.GetValueOrDefault(group)?.Name ?? parent?.Name ?? "";
        return ShortVariantName(GetSkinDisplayName(entry), parentName);
    }

    internal static string ShortVariantName(string name, string parentName)
    {
        name = name.Trim(); parentName = parentName.Trim();
        if (parentName.Length == 0 || !name.StartsWith(parentName, StringComparison.OrdinalIgnoreCase)) return name;
        string variant = name[parentName.Length..].Trim(' ', '·', '-', '—', '_', ':', '：', '/', '(', ')', '（', '）', '[', ']');
        return variant.Length == 0 ? UiLanguage.T("默认") : variant;
    }

    private string GetSkinVariantListName(SkinEntry entry) => GetSkinListName(entry, GetSkinVariantDisplayName(entry));

    private SkinEntry? CurrentSkinEntry()
    {
        if (_catalog == null) return null;
        int skin = _catalog.ActiveSkin ?? _catalog.Skin;
        string model = string.IsNullOrEmpty(_catalog.ActiveModel) ? _catalog.Model : _catalog.ActiveModel;
        return _catalog.Entries.FirstOrDefault(e => e.SkinNum == skin && string.Equals(e.Model, model, StringComparison.OrdinalIgnoreCase) && e.Gear == _catalog.Gear)
            ?? _catalog.Entries.FirstOrDefault(e => e.SkinNum == skin && string.Equals(e.Model, model, StringComparison.OrdinalIgnoreCase) && e.Gear < 0);
    }

    private void UpdateVariantSelection(string? selectedId = null)
    {
        bool updating = _updatingSelection; _updatingSelection = true;
        try
        {
            var parent = _entries.SelectedIndex >= 0 && _entries.SelectedIndex < _filtered.Count ? _filtered[_entries.SelectedIndex] : null;
            _variants = parent == null ? [] : SkinGroupEntries(SkinGroupNumber(parent));
            bool unchanged = _skinVariants.Items.Count == _variants.Count && _variants.Select((entry, i) =>
                _skinVariants.Items[i] is AntdUI.SelectItem item && Equals(item.Tag, entry.EntryId) && item.Text == GetSkinVariantListName(entry)).All(match => match);
            if (!unchanged)
            {
                _skinVariants.Items.Clear();
                foreach (var entry in _variants) _skinVariants.Items.Add(new AntdUI.SelectItem(GetSkinVariantListName(entry), entry.EntryId));
            }
            int selected = _variants.FindIndex(e => e.EntryId == (selectedId ?? parent?.EntryId));
            _skinVariants.SelectedIndex = selected;
            _skinVariants.Text = selected >= 0 ? GetSkinVariantDisplayName(_variants[selected]) : "";
        }
        finally { _updatingSelection = updating; }
    }

    private void SelectSkinInControls(SkinEntry? entry, bool clearSearch = false)
    {
        bool updating = _updatingSelection; _updatingSelection = true;
        try
        {
            if (clearSearch && _search.Text.Length > 0) _search.Text = "";
            FilterEntries();
            _entries.SelectedIndex = entry == null ? -1 : _filtered.FindIndex(e => SkinGroupNumber(e) == SkinGroupNumber(entry));
            UpdateVariantSelection(entry?.EntryId);
            UpdateSkinQualityColors(UiTheme.Palette);
        }
        finally { _updatingSelection = updating; }
    }
}
