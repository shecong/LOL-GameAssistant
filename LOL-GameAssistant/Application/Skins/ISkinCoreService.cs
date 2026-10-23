using LOL_GameAssistant.Domain.Skins;

namespace LOL_GameAssistant.Application.Skins;

public interface ISkinCoreService
{
    bool Enabled { get; }
    void SetEnabled(bool enabled);
    void InvalidateSession();
    Task<SkinCoreReply> GetCatalogAsync(CancellationToken cancellationToken = default);
    Task<SkinCoreReply> ApplyAsync(string session, string entryId, CancellationToken cancellationToken = default);
    Task<SkinCoreReply> RestoreAsync(string session, CancellationToken cancellationToken = default);
    Task<SkinCoreReply> StopAsync(string session, CancellationToken cancellationToken = default);
    Task<SkinCoreReply> GetRankBorderAsync(CancellationToken token = default) => Task.FromException<SkinCoreReply>(new SkinCoreException("rank_border_not_supported"));
    Task<SkinCoreReply> ApplyRankBorderAsync(string session, string profile, string id, CancellationToken token = default) => Task.FromException<SkinCoreReply>(new SkinCoreException("rank_border_not_supported"));
    Task<SkinCoreReply> RestoreRankBorderAsync(string session, string profile, CancellationToken token = default) => Task.FromException<SkinCoreReply>(new SkinCoreException("rank_border_not_supported"));
    Task<SkinCoreReply> GetObjectCatalogAsync(CancellationToken token = default) => Task.FromException<SkinCoreReply>(new SkinCoreException("objects_not_supported"));
    Task<SkinCoreReply> ApplyObjectsAsync(string session, int minionIndex, int wardSkin, CancellationToken token = default) => Task.FromException<SkinCoreReply>(new SkinCoreException("objects_not_supported"));
    Task<SkinCoreReply> RestoreObjectsAsync(string session, CancellationToken token = default) => Task.FromException<SkinCoreReply>(new SkinCoreException("objects_not_supported"));
    Task<SkinCoreReply> GetCameraAsync(CancellationToken token = default) => Task.FromException<SkinCoreReply>(new SkinCoreException("camera_not_supported"));
    Task<SkinCoreReply> SetCameraAsync(string session, float distance, CancellationToken token = default) => Task.FromException<SkinCoreReply>(new SkinCoreException("camera_not_supported"));
    Task<SkinCoreReply> RestoreCameraAsync(string session, CancellationToken token = default) => Task.FromException<SkinCoreReply>(new SkinCoreException("camera_not_supported"));
    Task<SkinCoreReply> MoveCameraAsync(string session, float distance, CancellationToken token = default) => Task.FromException<SkinCoreReply>(new SkinCoreException("camera_view_not_supported"));
}

public interface ISkinCoreTransport
{
    Task<SkinCoreReply> InvokeAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

public interface ISkinCoreModeSelector
{
    SkinCoreMode Mode { get; }
    void SetMode(SkinCoreMode mode);
}
