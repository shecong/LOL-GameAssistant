using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Domain.Coaching;
using Newtonsoft.Json.Linq;

namespace LOL_GameAssistant.Infrastructure.Sgp;

/// <summary>DETAILS 是历史时间线，独立于 SUMMARY 统计读取；不读取本局隐藏信息。</summary>
public sealed class SgpMatchTimelineService(SgpClient client) : IMatchTimelineService
{
    public async Task<JungleTimeline> GetAsync(long gameId, CancellationToken cancellationToken = default)
    {
        if (gameId <= 0) throw new ArgumentOutOfRangeException(nameof(gameId));
        var context = await client.GetContextAsync(cancellationToken).ConfigureAwait(false);
        var json = await client.ReadAsync(context,
            $"/match-history-query/v1/products/lol/{context.Platform}_{gameId}/DETAILS", false, cancellationToken).ConfigureAwait(false);
        return Parse(json, gameId, context.Platform);
    }

    internal static JungleTimeline Parse(JToken envelope, long gameId, string platform)
    {
        var json = envelope["json"] as JObject ?? envelope as JObject ?? throw new IOException("历史时间线格式无效。");
        string? matchId = envelope["metadata"]?.Value<string>("match_id") ?? json["metadata"]?.Value<string>("matchId");
        if (matchId != null && !string.Equals(matchId, $"{platform}_{gameId}", StringComparison.OrdinalIgnoreCase))
            throw new IOException("历史时间线对局或大区不匹配。");
        var data = json["info"] as JObject ?? json;
        if (data.Value<long?>("gameId") is long actualId && actualId != gameId)
            throw new IOException("历史时间线对局 ID 不匹配。");
        if (data["frames"] is not JArray frames || frames.Count == 0)
            throw new IOException("该历史对局未提供时间线位置数据。");
        var players = new Dictionary<int, List<JungleObservation>>();
        var events = new List<JungleHistoricalEvent>();
        void Add(int id, long timestamp, JToken? position, string evidence)
        {
            if (id is < 1 or > 10 || timestamp is < 60000 or > 480000 || position is not JObject p ||
                p.Value<double?>("x") is not double x || p.Value<double?>("y") is not double y) return;
            var point = new RiftPoint(x, y);
            if (!point.IsValid) return;
            if (!players.TryGetValue(id, out var list)) players[id] = list = [];
            list.Add(new((int)(timestamp / 1000), point, evidence));
        }
        foreach (var frame in frames.OfType<JObject>())
        {
            long timestamp = frame.Value<long?>("timestamp") ?? -1;
            if (frame["participantFrames"] is JObject participants)
                foreach (var property in participants.Properties())
                    if (int.TryParse(property.Name, out int id))
                        Add(id, timestamp, property.Value["position"], "历史位置采样");
            foreach (var ev in (frame["events"] as JArray ?? []).OfType<JObject>())
            {
                long time = ev.Value<long?>("timestamp") ?? -1;
                string type = ev.Value<string>("type") ?? "";
                if (time is < 60000 or > 480000) continue;
                RiftPoint? eventPosition = ev["position"] is JObject pos && pos.Value<double?>("x") is double px && pos.Value<double?>("y") is double py
                    && new RiftPoint(px, py).IsValid ? new(px, py) : null;
                if (type == "CHAMPION_KILL")
                {
                    // 击杀坐标是受害者位置，不能当作击杀者或远程助攻者的精确位置。
                    Add(ev.Value<int?>("victimId") ?? 0, time, ev["position"], "历史阵亡位置");
                    int killer = ev.Value<int?>("killerId") ?? 0;
                    if (killer > 0) events.Add(new((int)(time / 1000), [killer], eventPosition, "参与击杀（事件地点非击杀者精确站位）", "KILL"));
                    int[] assists = (ev["assistingParticipantIds"] as JArray ?? []).Values<int>().Where(id => id is >= 1 and <= 10).Distinct().ToArray();
                    if (assists.Length > 0) events.Add(new((int)(time / 1000), assists, eventPosition, "助攻击杀（不能确认助攻者站位）", "ASSIST"));
                }
                else if (type == "ELITE_MONSTER_KILL")
                {
                    int killer = ev.Value<int?>("killerId") ?? 0;
                    if (killer > 0) events.Add(new((int)(time / 1000), [killer], eventPosition,
                        "击杀野区目标：" + (ev.Value<string>("monsterType") ?? "未知目标"), "MONSTER"));
                }
            }
        }
        return new(gameId, players.ToDictionary(p => p.Key,
            p => (IReadOnlyList<JungleObservation>)p.Value.Distinct().OrderBy(v => v.Seconds).Take(80).ToArray()))
            { Events = events.OrderBy(v => v.Seconds).Take(300).ToArray() };
    }
}
