using LOL_GameAssistant.Application.Lobby;
using LOL_GameAssistant.Application.LeagueClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

public sealed class LcuPartyService(ILcuRequestSender lcu) : IPartyService
{
    private static readonly HashSet<string> Positions = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY", "FILL", "UNSELECTED"];
    private async Task<JToken?> Read(string route, CancellationToken token)
    {
        string? json = await lcu.GetStringAsync(route, token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JToken.Parse(json); } catch (JsonException) { return null; }
    }
    private static IEnumerable<JObject> Objects(JToken? data) => data is JArray a ? a.OfType<JObject>() : [];
    public async Task<IReadOnlyList<PartyQueue>> GetQueuesAsync(CancellationToken token = default)
    {
        var data = await Read("/lol-game-queues/v1/queues", token).ConfigureAwait(false);
        return Objects(data is JObject root ? root["queues"] : data).Select(q => new PartyQueue(q.Value<int?>("id") ?? 0,
            q.Value<string>("name") ?? q.Value<string>("description") ?? $"队列 {q["id"]}",
            q.Value<bool?>("isQueueEnabled") != false && !string.Equals(q.Value<string>("queueAvailability"), "Unavailable", StringComparison.OrdinalIgnoreCase),
            q.Value<string>("queueAvailability") ?? "客户端未提供开放原因"))
            .Where(q => q.Id > 0).OrderByDescending(q => q.Enabled).ThenBy(q => q.Id).ToArray();
    }
    public async Task<PartySnapshot> GetSnapshotAsync(CancellationToken token = default)
    {
        var lobbyTask = Read("/lol-lobby/v2/lobby", token);
        var friendsTask = Read("/lol-chat/v1/friends", token);
        var invitesTask = Read("/lol-lobby/v2/received-invitations", token);
        await Task.WhenAll(lobbyTask, friendsTask, invitesTask).ConfigureAwait(false);
        var lobby = lobbyTask.Result as JObject;
        var members = Objects(lobby?["members"]).Select(p => new PartyMember(p.Value<string>("puuid") ?? "",
            p.Value<string>("summonerName") ?? p.Value<string>("gameName") ?? "未知玩家", p.Value<bool?>("isLeader") == true,
            p.Value<string>("firstPositionPreference") ?? "未设置")).ToArray();
        var friends = Objects(friendsTask.Result).Select(p => new PartyFriend(p.Value<string>("puuid") ?? "",
            FriendName(p), p.Value<string>("availability") is "chat" or "online" or "away" or "dnd") { SummonerId = p.Value<long?>("summonerId") })
            .Where(f => f.Puuid.Length > 0).OrderByDescending(f => f.Online).ThenBy(f => f.Name).ToArray();
        var invitations = Objects(invitesTask.Result).Select(p => new PartyInvitation(p.Value<string>("invitationId") ?? p.Value<string>("id") ?? "",
            p.Value<string>("fromSummonerName") ?? p.SelectToken("fromSummoner.summonerName")?.ToString() ?? "大厅邀请",
            string.Equals(p.Value<string>("state") ?? "Pending", "Pending", StringComparison.OrdinalIgnoreCase)))
            .Where(i => i.Id.Length > 0 && i.Pending).ToArray();
        bool canStart = lobby?.Value<bool?>("canStartActivity") == true;
        bool invite = lobby?.SelectToken("localMember.canInviteOthers")?.Value<bool>() == true || lobby?.SelectToken("localMember.isLeader")?.Value<bool>() == true;
        var restrictions = Objects(lobby?["restrictions"]).Select(r => r.Value<string>("restrictionCode") ?? r.Value<string>("reason") ?? "存在队伍限制");
        string reason = string.Join("；", restrictions);
        return new(lobby?.SelectToken("gameConfig.queueId")?.Value<int>() ?? 0, invite, canStart, members, friends, invitations,
            lobby == null ? "尚未进入大厅，或大厅数据暂不可用" : canStart ? "队伍可以开始匹配" : reason.Length > 0 ? reason : "客户端尚未允许开始匹配：请检查队长、队伍人数、准备状态及模式资格");
    }
    private static string FriendName(JObject p)
    {
        string name = p.Value<string>("gameName") ?? p.Value<string>("name") ?? p.Value<string>("summonerName") ?? "未知好友";
        string? tag = p.Value<string>("gameTag") ?? p.Value<string>("tagLine");
        return string.IsNullOrWhiteSpace(tag) || name.Contains('#') ? name : name + "#" + tag;
    }
    public async Task<PartyActionResult> CreateAsync(int queueId, CancellationToken token = default)
    {
        var queues = await GetQueuesAsync(token).ConfigureAwait(false);
        if (!queues.Any(q => q.Id == queueId && q.Enabled)) return new(false, "该模式未被当前客户端列为可用，未创建大厅");
        return Result(await lcu.PostAsync("/lol-lobby/v2/lobby", JsonConvert.SerializeObject(new { queueId }), token).ConfigureAwait(false), "已创建大厅");
    }
    public async Task<PartyActionResult> InviteFriendAsync(string puuid, CancellationToken token = default)
    {
        var state = await GetSnapshotAsync(token).ConfigureAwait(false);
        if (!state.CanInvite) return new(false, "当前账号没有大厅邀请权限，请先创建大厅或取得邀请权限");
        if (!state.Friends.Any(f => f.Puuid == puuid && f.Online)) return new(false, "请选中当前客户端中可邀请的在线好友；不会根据招募昵称猜测玩家身份");
        if (state.Members.Any(m => m.Puuid == puuid)) return new(false, "该好友已在当前大厅中");
        var friend = state.Friends.First(f => f.Puuid == puuid);
        var invitation = new JObject { ["toPuuid"] = puuid }; if (friend.SummonerId is > 0) invitation["toSummonerId"] = friend.SummonerId;
        return Result(await lcu.PostAsync("/lol-lobby/v2/lobby/invitations", new JArray(invitation).ToString(Formatting.None), token).ConfigureAwait(false), "已提交好友邀请，等待对方接受");
    }
    public async Task<PartyActionResult> AcceptInvitationAsync(string id, CancellationToken token = default)
    {
        var state = await GetSnapshotAsync(token).ConfigureAwait(false);
        if (!state.Invitations.Any(i => i.Id == id && i.Pending)) return new(false, "邀请已失效或不属于当前账号");
        return Result(await lcu.PostAsync($"/lol-lobby/v2/received-invitations/{Uri.EscapeDataString(id)}/accept", "{}", token).ConfigureAwait(false), "已接受大厅邀请");
    }
    public async Task<PartyActionResult> SetPositionsAsync(string first, string second, CancellationToken token = default)
    {
        if (!Positions.Contains(first) || !Positions.Contains(second) || first == second && first != "FILL") return new(false, "请选择两个不同的位置，或使用补位");
        return Result(await lcu.PutAsync("/lol-lobby/v2/lobby/members/localMember/position-preferences", JsonConvert.SerializeObject(new { firstPreference = first, secondPreference = second }), token).ConfigureAwait(false), "已提交位置偏好");
    }
    public async Task<PartyActionResult> StartSearchAsync(CancellationToken token = default)
    {
        var state = await GetSnapshotAsync(token).ConfigureAwait(false);
        if (!state.CanStart) return new(false, state.Message);
        return Result(await lcu.PostAsync("/lol-lobby/v2/lobby/matchmaking/search", "{}", token).ConfigureAwait(false), "已开始匹配");
    }
    private static PartyActionResult Result(bool ok, string success) => new(ok, ok ? success : "客户端未接受操作，请核对当前阶段、权限和客户端版本");
}
