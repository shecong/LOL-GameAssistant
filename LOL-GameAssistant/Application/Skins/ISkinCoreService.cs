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
}

public interface ISkinCoreTransport
{
    Task<SkinCoreReply> InvokeAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}
