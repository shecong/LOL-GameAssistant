using LOL_GameAssistant.Application.Friends;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Domain.Friends;
using LOL_GameAssistant.Infrastructure.LeagueClient.Dto;
using Newtonsoft.Json;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>把 LCU 好友 JSON 转换为稳定的好友领域模型。</summary>
public sealed class LcuFriendDirectoryService : IFriendDirectoryService
{
    private readonly ILcuRequestSender _requestSender;

    public LcuFriendDirectoryService(ILcuRequestSender requestSender)
    {
        _requestSender = requestSender;
    }

    public async Task<IReadOnlyList<FriendProfile>> GetFriendsAsync(CancellationToken cancellationToken = default)
    {
        string? json = await _requestSender.GetStringAsync("/lol-chat/v1/friends", cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<FriendProfile>();

        var friends = JsonConvert.DeserializeObject<List<LcuFriendDto>>(json) ?? new List<LcuFriendDto>();
        return friends.Select(Map).ToList();
    }

    /// <summary>在基础设施边界归一化 LCU 的状态、昵称和对局 ID。</summary>
    private static FriendProfile Map(LcuFriendDto source)
    {
        string displayName = GetDisplayName(source);
        string? status = string.IsNullOrWhiteSpace(source.StatusMessage) ? source.Note : source.StatusMessage;
        int icon = source.Icon > 0 ? source.Icon : source.Lol?.Icon ?? 0;
        long? gameId = long.TryParse(source.Lol?.GameId, out long parsed) && parsed > 0 ? parsed : null;
        return new FriendProfile(
            source.Puuid,
            displayName,
            ToPresence(source.Availability),
            status,
            icon,
            source.Lol?.GameQueueType,
            gameId);
    }

    private static string GetDisplayName(LcuFriendDto source)
    {
        if (!string.IsNullOrWhiteSpace(source.DisplayName)) return source.DisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(source.GameName))
        {
            string name = source.GameName.Trim();
            return string.IsNullOrWhiteSpace(source.TagLine) || name.Contains('#')
                ? name
                : $"{name}#{source.TagLine.TrimStart('#')}";
        }
        return string.IsNullOrWhiteSpace(source.SummonerName) ? "未知好友" : source.SummonerName.Trim();
    }

    private static FriendPresence ToPresence(string? availability) => availability?.Trim().ToLowerInvariant() switch
    {
        "online" or "chat" => FriendPresence.Online,
        "away" => FriendPresence.Away,
        "dnd" => FriendPresence.DoNotDisturb,
        "mobile" => FriendPresence.Mobile,
        "ingame" or "ingameother" => FriendPresence.InGame,
        "spectator" => FriendPresence.Spectating,
        "offline" or "invisible" => FriendPresence.Offline,
        _ => FriendPresence.Unknown
    };
}
