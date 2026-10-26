namespace LOL_GameAssistant.Application.Profiles;

public enum ClientIdentityCategory { Border, Badge, Title, Banner }
public sealed record ClientIdentityChoice(ClientIdentityCategory Category, string Id, string Name, string Description,
    string ImagePath, bool? Owned);
public sealed record ClientIdentityState(string Session, string Title, string Banner, int Border, IReadOnlyList<long> Badges)
{
    public int BorderLevel { get; init; }
}
public sealed record ClientIdentityCatalog(ClientIdentityState State, IReadOnlyList<ClientIdentityChoice> Choices,
    IReadOnlyList<string> Notices);
public sealed record ClientIdentityResult(ClientIdentityState State, bool Verified, string Code, string Message);
public interface IClientIdentityService
{
    Task<ClientIdentityCatalog> ReadAsync(CancellationToken token = default);
    Task<byte[]?> GetImageAsync(ClientIdentityChoice choice, CancellationToken token = default);
    Task<ClientIdentityResult> ApplyAsync(string session, ClientIdentityCategory category, string id, int badgeSlot = 0, CancellationToken token = default);
    Task<ClientIdentityResult> RestoreAsync(string session, CancellationToken token = default);
}
