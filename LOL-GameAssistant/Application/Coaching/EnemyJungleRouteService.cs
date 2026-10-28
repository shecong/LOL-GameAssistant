using LOL_GameAssistant.Application.Lobby;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.Matches;

namespace LOL_GameAssistant.Application.Coaching;

/// <summary>限定读取最近 30 场中的最多 8 场峡谷打野时间线，按历史习惯生成可溯源预测。</summary>
public sealed class EnemyJungleRouteService(ILobbyService lobby, IPlayerProfileService profiles,
    IMatchHistoryService history, IMatchTimelineService timelines)
{
    private JungleRouteAnalysis? _cached;
    private DateTimeOffset _cachedAt;

    public async Task<JungleRouteAnalysis> AnalyzeAsync(string? selectedEnemy = null, bool force = false,
        CancellationToken cancellationToken = default)
    {
        string scope = PlayerHistorySession.Key;
        string? phase = await lobby.GetGameFlowPhaseAsync(cancellationToken).ConfigureAwait(false);
        if (phase is not ("InProgress" or "GameStart"))
        {
            _cached = null;
            return new("等待峡谷对局开始后自动分析敌方打野。");
        }
        var session = await lobby.GetCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
        if (session == null || !IsRift(session.GameMode, session.QueueId))
        {
            _cached = null;
            return new("仅支持召唤师峡谷常规匹配/排位；当前模式不生成打野路线。");
        }
        if (session.GameId <= 0) return new("等待客户端提供本局对局标识，暂不读取历史时间线。");
        var me = await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        bool teamOne = me != null && session.TeamOne.Any(p => p.Puuid == me.Puuid);
        bool teamTwo = me != null && session.TeamTwo.Any(p => p.Puuid == me.Puuid);
        if (teamOne == teamTwo) return new("尚未确认本人所在队伍，等待客户端阵容数据。");
        var enemies = (teamOne ? session.TeamTwo : session.TeamOne).Where(p => !p.IsBot && !string.IsNullOrWhiteSpace(p.Puuid))
            .Select(p => new JungleEnemy(p.Puuid, p.SummonerName, p.ChampionId, p.Position)).ToArray();
        var junglers = enemies.Where(p => JungleRoutePredictor.IsJungle(p.Position)).ToArray();
        var target = string.IsNullOrWhiteSpace(selectedEnemy) ? junglers.Length == 1 ? junglers[0] : null
            : enemies.FirstOrDefault(p => p.Puuid == selectedEnemy);
        int team = teamOne ? 200 : 100;
        if (target == null) return new("客户端未明确唯一敌方打野，请在上方选择玩家确认。"){ Enemies = enemies, TeamId = team };
        string key = $"{scope}:{session.GameId}:{team}:{target.Puuid}:{target.ChampionId}:" +
            string.Join(",", session.TeamOne.Concat(session.TeamTwo).Select(p => p.Puuid));
        if (!force && _cached?.ContextKey == key && DateTimeOffset.UtcNow - _cachedAt <
            (_cached.Prediction == null ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(15))) return _cached;

        var page = await history.GetPageAsync(target.Puuid, 0, 29, cancellationToken).ConfigureAwait(false);
        var candidates = (page?.Games?.Games ?? []).Where(g => IsRift(g.GameMode, g.QueueId) && !g.IsCustomGame() && g.IsCompletedGame())
            .OrderByDescending(g => g.GameCreation).DistinctBy(g => g.GameId)
            .Select(g => (Game: g, Player: g.GetParticipant(target.Puuid)))
            .Where(p => p.Player is { participantId: >= 1 and <= 10, teamId: 100 or 200 } &&
                (p.Player.Spell1Id == 11 || p.Player.Spell2Id == 11))
            .Take(8).ToArray();
        var samples = new List<JungleRouteSample>();
        int unavailable = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var timeline = await timelines.GetAsync(candidate.Game.GameId, cancellationToken).ConfigureAwait(false);
                if (timeline.GameId != candidate.Game.GameId) throw new IOException("时间线对局不匹配。");
                if (!timeline.Players.TryGetValue(candidate.Player!.participantId, out var points)) { unavailable++; continue; }
                var usable = points.Where(p => p.Seconds is >= 60 and <= 480 && p.Position.IsValid &&
                    p.Position.Distance(new(500, 500)) > 2300 && p.Position.Distance(new(14500, 14500)) > 2300).ToArray();
                if (usable.Length < 3) { unavailable++; continue; }
                samples.Add(new(candidate.Game.GameId, candidate.Player.championId, candidate.Player.teamId, candidate.Game.GameCreation, usable)
                    { Events = timeline.Events.Where(ev => ev.Participants.Contains(candidate.Player.participantId)).ToArray() });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is IOException or HttpRequestException or NotSupportedException or Newtonsoft.Json.JsonException or OperationCanceledException)
            { unavailable++; }
        }
        cancellationToken.ThrowIfCancellationRequested();
        // 阵容/会话变化后的结果不能出现在下一局地图上。
        var current = await lobby.GetCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
        if (scope != PlayerHistorySession.Key || current == null || current.GameId != session.GameId ||
            !(teamOne ? current.TeamTwo : current.TeamOne).Any(p => p.Puuid == target.Puuid && p.ChampionId == target.ChampionId) ||
            await lobby.GetGameFlowPhaseAsync(cancellationToken).ConfigureAwait(false) is not ("InProgress" or "GameStart"))
            return new("对局已变化，等待重新识别敌方打野。");
        var prediction = JungleRoutePredictor.Predict(samples, target.ChampionId, team);
        string status = prediction == null ? "数据不足：近期打野对局缺少可用开局位置时间线，暂不生成预测。"
            : $"已分析 {samples.Count} 场历史打野对局 · 前 8 分钟 · 预测可信度：{prediction.Confidence}";
        _cached = new(status) { ContextKey = key, Enemy = target, Enemies = enemies, TeamId = team,
            Samples = samples, Prediction = prediction, UnavailableGames = unavailable };
        _cachedAt = DateTimeOffset.UtcNow;
        return _cached;
    }

    internal static bool IsRift(string mode, int queue) =>
        (mode.Equals("CLASSIC", StringComparison.OrdinalIgnoreCase) || mode.Equals("CLASSIC SR", StringComparison.OrdinalIgnoreCase)) &&
        queue is 2 or 4 or 6 or 14 or 400 or 410 or 420 or 430 or 440 or 490;
}
