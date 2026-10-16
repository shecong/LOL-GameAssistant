using LOL.Lounge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace LOL_GameApi.Lounge;

[Authorize(AuthenticationSchemes = LoungeAuthentication.SchemeName)]
public sealed class LoungeHub(LoungeRoom room, LoungeStore store, ILogger<LoungeHub> logger) : Hub
{
    private string Session => Context.UserIdentifier ?? throw new HubException("会话无效。");

    public override async Task OnConnectedAsync()
    {
        room.Check(Session, "connect", 15, TimeSpan.FromMinutes(1));
        room.Connect(Context.ConnectionId, Session, Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        logger.LogInformation("Lounge connected session {Session}", Session);
        await Clients.All.SendAsync("Members", room.Members());
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        room.Remove(Context.ConnectionId);
        await Clients.All.SendAsync("Members", room.Members());
        await base.OnDisconnectedAsync(exception);
    }

    public async Task UpdatePresence(LoungePresence presence)
    {
        room.Check(Session, "presence", 30, TimeSpan.FromMinutes(1));
        if (presence is null) throw new HubException("状态数据无效。");
        var previous = room.Get(Context.ConnectionId, allowStale: true);
        if (room.Update(Context.ConnectionId, presence)) await Clients.All.SendAsync("Members", room.Members());
        if (LoungeRules.PlayerId(previous) != LoungeRules.PlayerId(room.Get(Context.ConnectionId)) &&
            room.ShouldAnnounceOnline(Context.ConnectionId))
        {
            var player = room.Get(Context.ConnectionId);
            string rank = string.IsNullOrWhiteSpace(player.Rank) ? "" : $" · {player.Rank}";
            var message = await store.AppendAsync("online:" + Guid.NewGuid().ToString("N"), "", Session,
                player.Name, player.Region, "online", $"👋 【{player.Name}】上线了！【{player.Region}{rank}】欢迎来到休息室。", Context.ConnectionAborted);
            if (message != null) await Clients.All.SendAsync("Message", message);
        }
    }

    public async Task<LoungeSnapshot> Sync(long after)
    {
        room.Check(Session, "sync", 30, TimeSpan.FromMinutes(1));
        return new(await store.ReadAsync(Math.Max(0, after), Context.ConnectionAborted), room.Members(),
            await store.ReadDeletedIdsAsync(Context.ConnectionAborted), LoungeRules.ProtocolVersion,
            await store.ReadRecruitmentsAsync(Context.ConnectionAborted));
    }

    public async Task<LoungeMessage> SendMessage(LoungeSend request)
    {
        room.Check(Session, "chat", 1, TimeSpan.FromSeconds(2), writing: true);
        var player = room.Get(Context.ConnectionId);
        if (string.IsNullOrWhiteSpace(player.Puuid)) throw new HubException("请先登录 LOL 后再发言。");
        (string kind, string text) content;
        try { content = LoungeContent.ValidateSend(request); }
        catch (ArgumentException ex) { throw new HubException(ex.Message); }
        if ((request.Mentions ?? []).Any(id => !room.Members().Any(m => m.Id == id)))
            throw new HubException("提及的成员已离线，请重新选择。");
        var message = await store.AppendAsync($"chat:{Session}:{request.ClientId}", request.ClientId,
            Session, player.Name, player.Region, content.kind, content.text, Context.ConnectionAborted, LoungeRules.PlayerId(player));
        await Clients.All.SendAsync("Message", message);
        if (content.kind == "recruit")
        {
            var state = (await store.ReadRecruitmentsAsync(Context.ConnectionAborted)).FirstOrDefault(s => s.MessageId == message!.Id);
            if (state != null) await Clients.All.SendAsync("Recruitment", state);
        }
        return message!;
    }

    public Task<LoungeMessage> PublishRecruitment(LoungeSend request)
    {
        if (request?.Kind != "recruit") throw new HubException("请选择有效的开黑招募。");
        return SendMessage(request);
    }

    public async Task<LoungeRecruitmentState> ChangeRecruitment(long id, string action)
    {
        room.Check(Session, "recruitment", 12, TimeSpan.FromMinutes(1), writing: true);
        var player = room.Get(Context.ConnectionId);
        if (string.IsNullOrWhiteSpace(player.Puuid)) throw new HubException("请先登录 LOL 再操作招募。");
        LoungeRecruitmentState state;
        try
        {
            state = await store.ChangeRecruitmentAsync(id,
                new(LoungeRules.PlayerId(player), player.Name, player.Region, player.Status, player.Rank), action, Context.ConnectionAborted);
        }
        catch (ArgumentException ex) { throw new HubException(ex.Message); }
        await Clients.All.SendAsync("Recruitment", state);
        return state;
    }

    public async Task ReportHighlight(LoungeHighlight request)
    {
        room.Check(Session, "highlight", 8, TimeSpan.FromMinutes(1), writing: true);
        var player = room.Get(Context.ConnectionId);
        if (request == null || !player.ShareActivity || string.IsNullOrWhiteSpace(player.Puuid) ||
            string.IsNullOrWhiteSpace(player.Platform) || request.Puuid != player.Puuid || request.Platform != player.Platform ||
            request.GameId <= 0 || !LoungeRules.IsRecent(request.FinishedAt, DateTimeOffset.UtcNow) ||
            !LoungeRules.Modes.TryGetValue(request.QueueId, out var mode) || request.Kind is not ("pentakill" or "legendary" or "promotion") ||
            (request.Kind == "promotion" && (request.QueueId is not (420 or 440) || string.IsNullOrWhiteSpace(request.Detail) || request.Detail.Length > 80)))
            throw new HubException("精彩战绩不符合播报条件。");
        string achievement = request.Kind switch
        {
            "pentakill" => "斩获五杀！全场焦点，实至名归！",
            "legendary" => "打出超神表现！这波状态拉满！",
            _ => "成功晋级至【" + LoungeRules.Clean(request.Detail, 80) + "】！恭喜更进一步！"
        };
        var message = await store.AppendAsync($"highlight:{LoungeRules.PlayerId(player)}:{request.GameId}:{request.Kind}", "",
            Session, player.Name, player.Region, "highlight", $"🏆 【{player.Name}】（{player.Region}）在【{mode}】{achievement}", Context.ConnectionAborted);
        if (message != null) await Clients.All.SendAsync("Message", message);
    }

    public async Task ReportVictory(LoungeVictory request)
    {
        room.Check(Session, "victory", 4, TimeSpan.FromMinutes(1), writing: true);
        var player = room.Get(Context.ConnectionId);
        if (request is null || !player.ShareActivity || string.IsNullOrWhiteSpace(player.Puuid) ||
            string.IsNullOrWhiteSpace(player.Platform) || request.Puuid != player.Puuid || request.Platform != player.Platform ||
            request.GameId <= 0 || !LoungeRules.IsRecent(request.FinishedAt, DateTimeOffset.UtcNow) ||
            !LoungeRules.Modes.TryGetValue(request.QueueId, out var mode))
            throw new HubException("胜局数据不符合播报条件。");
        string key = $"win:{LoungeRules.PlayerId(player)}:{request.GameId}";
        string[] party = (request.PartyNames ?? []).Take(4).Select(n => LoungeRules.Clean(n, 80).Trim())
            .Where(n => n.Length > 0 && n != player.Name).Distinct().ToArray();
        string together = party.Length == 0 ? "" : $"与【{string.Join("】、【", party)}】组队开黑，";
        var message = await store.AppendAsync(key, "", Session, player.Name, player.Region, "victory",
            $"🎉 【{player.Name}】（{player.Region}）{together}拿下了一场【{mode}】胜利！漂亮，下一把继续！", Context.ConnectionAborted);
        if (message != null) await Clients.All.SendAsync("Message", message);
    }
}
