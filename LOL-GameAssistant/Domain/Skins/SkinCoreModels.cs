namespace LOL_GameAssistant.Domain.Skins;

public enum SkinCoreMode { Classic, KernelRelay }

public enum SkinPickerCategory { Hero, Ward, Minion }

public sealed record RankBorderEntry
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
}

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
    public bool RankBorderSupported { get; init; }
    public string RankBorderProfile { get; init; } = "";
    public string RankBorderId { get; init; } = "";
    public IReadOnlyList<RankBorderEntry> RankBorderEntries { get; init; } = [];
    public bool? RankBorderRestoreVerified { get; init; }
    public string Backend { get; init; } = "";
    public float CameraDistance { get; init; }
    public float CameraDefaultViewDistance { get; init; }
    public float? CameraViewDistance { get; init; }
    public float? CameraViewTarget { get; init; }
    public string CameraViewId { get; init; } = "";
    public string CameraId { get; init; } = "";
    public bool? CameraRestoreVerified { get; init; }
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
    public int Pid { get; init; }
    public bool? GameRunning { get; init; }
    public bool? ModuleLoaded { get; init; }
    public bool? ModuleUnloadVerified { get; init; }
    public bool? HeroRestoreVerified { get; init; }
    public bool? ObjectRestoreVerified { get; init; }
    public string Error { get; init; } = "";
    public string Stage { get; init; } = "";
    public int? Win32 { get; init; }
    public int? Phase { get; init; }
    public uint ThreadId { get; init; }
    public string HostArchitecture { get; init; } = "";
    public long? ElapsedMs { get; init; }
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

public sealed record SkinCoreFailureDetails
{
    public string Stage { get; init; } = "";
    public int? Win32 { get; init; }
    public string RequestId { get; init; } = "";
    public int? Phase { get; init; }
    public int Pid { get; init; }
    public uint ThreadId { get; init; }
    public string HostArchitecture { get; init; } = "";
    public long? NativeElapsedMs { get; init; }
    public long ElapsedMs { get; init; }
    public string Command { get; init; } = "";
    public string Backend { get; init; } = "";
    public string HostPath { get; init; } = "";
    public int? ExitCode { get; init; }
    public string ProcessArchitecture { get; init; } = "";
    public string OsArchitecture { get; init; } = "";
    public string OsDescription { get; init; } = "";
}

public sealed class SkinCoreException(string code, bool outcomeUnknown = false,
    SkinCoreFailureDetails? details = null) : Exception(code)
{
    public string Code { get; } = code;
    public bool OutcomeUnknown { get; } = outcomeUnknown;
    public SkinCoreFailureDetails? Details { get; internal set; } = details;
}
