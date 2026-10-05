using LOL_GameAssistant.Domain.ChampionSelect;
using LOL_GameAssistant.Entity;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>HTTP 与选人推送共用快照映射；推送无需再请求选人接口。</summary>
public static class ChampionSelectionSnapshotMapper
{
    /// <summary>解析客户端选人事件中的会话数据。</summary>
    public static ChampionSelectionSnapshot? ParseEvent(string data)
    {
        try
        {
            var json = JObject.Parse(data);
            if (json["benchChampions"] == null && json["myTeam"] == null) return null;
            var session = json.ToObject<ChampSelectSession>();
            return session == null ? null : Map(session);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>将外部选人数据转换为统一的选人快照。</summary>
    public static ChampionSelectionSnapshot Map(ChampSelectSession session) => new()
    {
        LocalPlayerCellId = session.LocalPlayerCellId,
        BenchChampionIds = (session.BenchChampions ?? [])
            .Where(champion => champion != null).Select(champion => champion.ChampionId)
            .Where(id => id > 0).Distinct().ToArray(),
        Actions = (session.Actions ?? [])
            .Select(round => (IReadOnlyList<ChampionSelectionAction>)(round ?? [])
                .Where(action => action != null).Select(action => new ChampionSelectionAction
                {
                    ActorCellId = action.ActorCellId, ChampionId = action.ChampionId,
                    IsAllyAction = action.IsAllyAction, IsInProgress = action.IsInProgress,
                    Completed = action.Completed, Type = action.Type ?? ""
                }).ToArray()).ToArray(),
        MyTeam = MapMembers(session.MyTeam),
        TheirTeam = MapMembers(session.TheirTeam)
    };

    /// <summary>将外部队伍成员转换为选人快照成员。</summary>
    private static IReadOnlyList<ChampionSelectionMember> MapMembers(IEnumerable<ChampSelectTeamMember>? members) =>
        (members ?? []).Where(member => member != null).Select(member => new ChampionSelectionMember
        {
            CellId = member.CellId, ChampionId = member.ChampionId,
            AssignedPosition = member.AssignedPosition ?? "", IsAutofilled = member.IsAutofilled,
            ChampionPickIntent = member.ChampionPickIntent, Puuid = member.Puuid ?? ""
        }).ToArray();
}
