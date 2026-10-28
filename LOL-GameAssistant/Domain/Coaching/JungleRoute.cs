namespace LOL_GameAssistant.Domain.Coaching;

public sealed record RiftPoint(double X, double Y)
{
    public bool IsValid => double.IsFinite(X) && double.IsFinite(Y) && X is >= 0 and <= 15000 && Y is >= 0 and <= 15000;
    public RiftPoint Mirror() => new(15000 - X, 15000 - Y);
    public double Distance(RiftPoint other) => Math.Sqrt(Math.Pow(X - other.X, 2) + Math.Pow(Y - other.Y, 2));
}

public sealed record JungleObservation(int Seconds, RiftPoint Position, string Evidence);
public sealed record JungleHistoricalEvent(int Seconds, IReadOnlyList<int> Participants, RiftPoint? Position, string Description, string Kind = "");
public sealed record JungleTimeline(long GameId, IReadOnlyDictionary<int, IReadOnlyList<JungleObservation>> Players)
{
    public IReadOnlyList<JungleHistoricalEvent> Events { get; init; } = [];
}
public sealed record JungleRouteSample(long GameId, int ChampionId, int TeamId, long CreatedAt,
    IReadOnlyList<JungleObservation> Points)
{
    public IReadOnlyList<JungleHistoricalEvent> Events { get; init; } = [];
}
public sealed record JungleRoutePrediction(IReadOnlyList<JungleObservation> Points, int SupportingGames, int TotalGames,
    bool SameChampion, string Confidence, string OpeningRegion)
{
    public long RepresentativeGameId { get; init; }
    public string ActivityHint { get; init; } = "历史样本未提供可靠的早期交战事件。";
}

/// <summary>仅从历史位置采样生成开局习惯；连线是采样点插值，不代表精确的清野顺序或本局位置。</summary>
public static class JungleRoutePredictor
{
    public static bool IsJungle(string? position) => (position ?? "").Trim().ToUpperInvariant() is "JUNGLE" or "JNG";

    public static IReadOnlyList<JungleObservation> Normalize(JungleRouteSample sample, int targetTeam) =>
        sample.Points.Where(p => p.Seconds is >= 60 and <= 480 && p.Position.IsValid)
            .OrderBy(p => p.Seconds).Select(p => sample.TeamId == targetTeam ? p : p with { Position = p.Position.Mirror() }).ToArray();

    public static JungleRoutePrediction? Predict(IReadOnlyList<JungleRouteSample> samples, int championId, int targetTeam)
    {
        if (targetTeam is not (100 or 200)) return null;
        var valid = samples.Where(s => s.TeamId is 100 or 200 && Normalize(s, targetTeam).Count >= 3)
            .DistinctBy(s => s.GameId).ToArray();
        var same = valid.Where(s => s.ChampionId == championId).ToArray();
        var pool = same.Length >= 2 ? same : valid;
        if (pool.Length == 0) return null;
        // 只使用前 3 分钟实际观察到的第一处野区位置，不把它称为“第一个营地”。
        var routes = pool.Select(s => (Sample: s, Points: Normalize(s, targetTeam)))
            .Select(s => (s.Sample, s.Points, Opening: s.Points.FirstOrDefault(p => p.Seconds <= 180 && IsJungleArea(p.Position))))
            .Where(s => s.Opening != null).ToArray();
        if (routes.Length == 0) return null;
        var group = routes.GroupBy(s => Region(s.Opening!.Position)).OrderByDescending(g => g.Count()).First();
        // 选择组内与其他历史路线距离最小的代表，避免平均坐标生成穿墙“路线”。
        var representative = group.OrderBy(route => group.Sum(other => RouteDistance(route.Points, other.Points))).First();
        int support = group.Count(), total = routes.Length;
        string confidence = total < 3 ? "低（样本不足 3 场）" : support * 1d / total >= .7 && support >= 4 ? "中（历史起始区域较一致）" : "低（历史路线有分歧）";
        var fights = pool.Select(sample => (Sample: sample, Event: sample.Events
            .Where(ev => ev.Kind is "KILL" or "ASSIST" && ev.Position?.IsValid == true)
            .OrderBy(ev => ev.Seconds).FirstOrDefault())).Where(p => p.Event != null)
            .GroupBy(p => Region(p.Sample.TeamId == targetTeam ? p.Event!.Position! : p.Event!.Position!.Mirror()))
            .OrderByDescending(g => g.Count()).FirstOrDefault();
        string activity = fights == null ? "历史样本未提供可靠的早期交战事件。" :
            $"历史首次参与击杀/助攻：{fights.Key}，{fights.Min(p => p.Event!.Seconds) / 60}:{fights.Min(p => p.Event!.Seconds) % 60:D2}–{fights.Max(p => p.Event!.Seconds) / 60}:{fights.Max(p => p.Event!.Seconds) % 60:D2}（{fights.Count()}/{pool.Length} 场）";
        return new(representative.Points, support, total, same.Length >= 2, confidence, group.Key)
            { RepresentativeGameId = representative.Sample.GameId, ActivityHint = activity };
    }

    private static double RouteDistance(IReadOnlyList<JungleObservation> a, IReadOnlyList<JungleObservation> b) =>
        a.Sum(p => b.Min(q => p.Position.Distance(q.Position) + Math.Abs(p.Seconds - q.Seconds) * 25));
    public static bool IsJungleArea(RiftPoint p) =>
        p.Distance(new(500, 500)) > 2300 && p.Distance(new(14500, 14500)) > 2300 &&
        p.X is > 1800 and < 13200 && p.Y is > 1800 and < 13200 && Math.Abs(p.X - p.Y) > 1300;
    public static string Region(RiftPoint p) => (p.Y > p.X ? "上半区" : "下半区") + (p.X + p.Y < 15000 ? " · 蓝方侧" : " · 红方侧");
}

public sealed record JungleEnemy(string Puuid, string Name, int ChampionId, string Position);
public sealed record JungleRouteAnalysis(string Status)
{
    public string ContextKey { get; init; } = "";
    public JungleEnemy? Enemy { get; init; }
    public IReadOnlyList<JungleEnemy> Enemies { get; init; } = [];
    public int TeamId { get; init; }
    public IReadOnlyList<JungleRouteSample> Samples { get; init; } = [];
    public JungleRoutePrediction? Prediction { get; init; }
    public int UnavailableGames { get; init; }
}
