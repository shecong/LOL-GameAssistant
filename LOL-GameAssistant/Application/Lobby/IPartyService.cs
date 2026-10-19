namespace LOL_GameAssistant.Application.Lobby;

public sealed record PartyQueue(int Id, string Name, bool Enabled, string Reason);
public sealed record PartyMember(string Puuid, string Name, bool Leader, string Position);
public sealed record PartyFriend(string Puuid, string Name, bool Online) { public long? SummonerId { get; init; } }
public sealed record PartyInvitation(string Id, string Name, bool Pending);
public sealed record PartySnapshot(int QueueId, bool CanInvite, bool CanStart, IReadOnlyList<PartyMember> Members,
    IReadOnlyList<PartyFriend> Friends, IReadOnlyList<PartyInvitation> Invitations, string Message);
public sealed record PartyActionResult(bool Succeeded, string Message);

public interface IPartyService
{
    Task<IReadOnlyList<PartyQueue>> GetQueuesAsync(CancellationToken token = default);
    Task<PartySnapshot> GetSnapshotAsync(CancellationToken token = default);
    Task<PartyActionResult> CreateAsync(int queueId, CancellationToken token = default);
    Task<PartyActionResult> InviteFriendAsync(string puuid, CancellationToken token = default);
    Task<PartyActionResult> AcceptInvitationAsync(string id, CancellationToken token = default);
    Task<PartyActionResult> SetPositionsAsync(string first, string second, CancellationToken token = default);
    Task<PartyActionResult> StartSearchAsync(CancellationToken token = default);
}
