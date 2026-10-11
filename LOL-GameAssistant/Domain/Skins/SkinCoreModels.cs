namespace LOL_GameAssistant.Domain.Skins;

public sealed record SkinEntry
{
    public string EntryId { get; init; } = "";
    public int Index { get; init; }
    public int Gear { get; init; } = -1;
    public int SkinNum { get; init; }
    public string Name { get; init; } = "";
    public string Model { get; init; } = "";
    public string? GearName { get; init; }
}

public sealed record SkinCoreReply
{
    public int LaneCount { get; init; }
    public int OwnWardCount { get; init; }
    public int BaselineMinionSkin { get; init; } = -1;
    public int BaselineWardSkin { get; init; } = -1;
    public int ObjectTargetCount { get; init; }
    public int ObjectChangedCount { get; init; }
    public IReadOnlyList<SkinEntry> WardEntries { get; init; } = [];
    public bool Ok { get; init; }
    public bool Independent { get; init; }
    public bool OriginalRequired { get; init; } = true;
    public bool Invoked { get; init; }
    public bool StateVerified { get; init; }
    public bool? TransportHookReleased { get; init; }
    public bool? ModulePinned { get; init; }
    public string Error { get; init; } = "";
    public string Stage { get; init; } = "";
    public string Session { get; init; } = "";
    public string RequestId { get; init; } = "";
    public string ReferenceSha256 { get; init; } = "";
    public string Model { get; init; } = "";
    public string ActiveModel { get; init; } = "";
    public int Skin { get; init; }
    public int? ActiveSkin { get; init; }
    public int Gear { get; init; } = -1;
    public IReadOnlyList<SkinEntry> Entries { get; init; } = [];
}

public sealed class SkinCoreException(string code, bool outcomeUnknown = false) : Exception(code)
{
    public string Code { get; } = code;
    public bool OutcomeUnknown { get; } = outcomeUnknown;
}
