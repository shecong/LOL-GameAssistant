namespace LOL_GameAssistant.Domain.Skins;

public static class SkinEntryCycle
{
    public static SkinEntry Choose(SkinCoreReply catalog, int direction)
    {
        if (catalog.Entries.Count == 0 || direction is not (-1 or 1)) throw new SkinCoreException("invalid_catalog");
        int current = -1;
        for (int i = 0; i < catalog.Entries.Count; i++)
        {
            var item = catalog.Entries[i];
            if (item.SkinNum == catalog.Skin && item.Model == catalog.ActiveModel && item.Gear == catalog.Gear) { current = i; break; }
        }
        if (current < 0)
            for (int i = 0; i < catalog.Entries.Count; i++)
                if (catalog.Entries[i].SkinNum == catalog.Skin && catalog.Entries[i].Model == catalog.ActiveModel && catalog.Entries[i].Gear == -1) { current = i; break; }
        if (current < 0) return direction > 0 ? catalog.Entries[0] : catalog.Entries[^1];
        return catalog.Entries[(current + direction + catalog.Entries.Count) % catalog.Entries.Count];
    }
}
