namespace LOL_GameAssistant.Application.Skins;

/// <summary>
/// 文件层换肤引擎（CSLOL 路线）。与 ISkinCoreService 的内存层并列，互斥使用。
/// </summary>
public interface ICslolSkinService
{
    /// <summary>mod-tools.exe / cslol-dll.dll / hashes / 皮肤包 是否齐备。</summary>
    bool IsAvailable { get; }

    /// <summary>runoverlay 常驻进程是否在跑。</summary>
    bool IsRunning { get; }

    /// <summary>已加载的皮肤目录（来自 manifest，不读加密包）。</summary>
    IReadOnlyList<CslolSkinEntry> Catalog { get; }
    string CatalogPatch => "";

    /// <summary>进度文案（"正在导入皮肤资源..."）。</summary>
    event Action<string>? StatusChanged;

    Task<IReadOnlyList<CslolSkinEntry>> LoadCatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// import → mkoverlay → runoverlay。游戏进程必须在启动前调用。
    /// </summary>
    Task<CslolApplyResult> ApplyAsync(
        string gamePath,
        IReadOnlyList<CslolSkinEntry> entries,
        bool ignoreConflict = false,
        CancellationToken cancellationToken = default);

    /// <summary>停止 runoverlay 并清理 mod-tools 进程。</summary>
    Task<CslolApplyResult> StopAsync();
}

/// <summary>一条皮肤（对应加密包里的一个 fantome）。</summary>
public sealed record CslolSkinEntry
{
    public string ChampionKey { get; init; } = "";
    public int ChampionId { get; init; }
    public string SkinName { get; init; } = "";
    public int SkinId { get; init; }
    public int SkinNum { get; init; }
    public string? ChromaId { get; init; }
    public string? ChromaName { get; init; }
    public string FileName { get; init; } = "";
    public long Size { get; init; }
    public string Sha256 { get; init; } = "";

    /// <summary>显示名：有炫彩时带炫彩名。</summary>
    public string DisplayName => ChromaId is null ? SkinName : $"{SkinName} {ChromaName}";

    /// <summary>同一英雄只能注入一个，用这个做冲突判定。</summary>
    public string ConflictKey => ChampionKey;
}

public sealed record CslolApplyResult
{
    public bool Ok { get; init; }
    public string Stage { get; init; } = "";
    public string Message { get; init; } = "";
    public int ImportedMods { get; init; }
    public string OverlayPath { get; init; } = "";
    public int? RunoverlayPid { get; init; }
}
