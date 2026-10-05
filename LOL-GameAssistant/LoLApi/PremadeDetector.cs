using LOL_GameAssistant.Entity;
using System.Collections.Concurrent;

namespace LOL_GameAssistant.LoLApi
{
    /// <summary>
    /// 开黑（预组队）检测器：
    /// 通过每位玩家最近 20 场对局的战绩摘要，统计“与当前对局其他玩家同队”的次数，
    /// 整组共同同队 ≥ 2 次时标记为疑似开黑，不作本局组队断言。
    /// 算法参考社区项目（rank-analysis / Yuumi）的做法，无需拉取对局详情。
    /// </summary>
    public static class PremadeDetector
    {
        /// <summary>最近战绩拉取场数（足够覆盖近几天的开黑记录）。</summary>
        private const int HistoryCount = 20;

        /// <summary>判定为开黑的“同队场次”阈值。</summary>
        private const int SameTeamThreshold = 2;

        /// <summary>单玩家战绩摘要缓存（5 分钟），避免自动刷新时重复请求。</summary>
        private static readonly ConcurrentDictionary<string, (DateTime Time, GameHeadModel.MatchHistoryResponse? Data)>
            HistoryCache = new();

        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

        /// <summary>
        /// 开黑小组信息。
        /// </summary>
        public class PremadeGroupInfo
        {
            /// <summary>组编号（从 1 开始，仅用于展示区分）。</summary>
            public int Index { get; set; }

            /// <summary>组内玩家 puuid。</summary>
            public List<string> Puuids { get; set; } = new();

            /// <summary>组内玩家名称。</summary>
            public List<string> Names { get; set; } = new();

            /// <summary>所在队伍（0=蓝方，1=红方）。</summary>
            public int TeamIndex { get; set; }
        }

        /// <summary>
        /// 开黑检测结果。
        /// </summary>
        public class PremadeResult
        {
            /// <summary>全部开黑小组（按队伍分组排序）。</summary>
            public List<PremadeGroupInfo> Groups { get; set; } = new();

            /// <summary>puuid → 开黑小组（不在任何小组的玩家无记录）。</summary>
            public Dictionary<string, PremadeGroupInfo> GroupByPuuid { get; set; } = new();

            /// <summary>
            /// 生成某支队伍的开黑人数摘要，如“2+2”表示两支双排，返回空串表示无开黑。
            /// </summary>
            public string GetTeamSummary(int teamIndex)
            {
                var sizes = Groups
                    .Where(g => g.TeamIndex == teamIndex)
                    .Select(g => g.Puuids.Count)
                    .OrderByDescending(x => x)
                    .ToList();
                return sizes.Count == 0 ? "" : string.Join("+", sizes);
            }

            /// <summary>
            /// 返回队伍的排队状态标签。无固定队友时为“单排”，单个固定小组时显示具体人数，
            /// 存在多个小组时以“多排 2+2”等形式标识。
            /// </summary>
            public string GetTeamQueueStatus(int teamIndex)
            {
                var sizes = Groups
                    .Where(group => group.TeamIndex == teamIndex)
                    .Select(group => group.Puuids.Count)
                    .OrderByDescending(size => size)
                    .ToList();
                if (sizes.Count == 0) return "单排";

                if (sizes.Count == 1)
                {
                    return sizes[0] switch
                    {
                        2 => "双排",
                        3 => "三排",
                        4 => "四排",
                        5 => "五排",
                        _ => "多排"
                    };
                }

                return $"多排 {string.Join("+", sizes)}";
            }

            /// <summary>
            /// 生成可用于悬停说明的队伍检测详情。
            /// </summary>
            public string GetTeamQueueDetail(int teamIndex)
            {
                var groups = Groups.Where(group => group.TeamIndex == teamIndex).ToList();
                if (groups.Count == 0) return "近期战绩中未检测到固定同队关系";

                return string.Join("；", groups.Select(group =>
                    $"{group.Puuids.Count} 人组队：{string.Join("、", group.Names)}"));
            }
        }

        /// <summary>
        /// 检测两支队伍的玩家开黑情况。
        /// </summary>
        /// <param name="team1">蓝方玩家 (puuid, 名称)。</param>
        /// <param name="team2">红方玩家 (puuid, 名称)。</param>
        public static async Task<PremadeResult> DetectAsync(
            List<(string Puuid, string Name)> team1,
            List<(string Puuid, string Name)> team2)
        {
            var histories = await FetchHistoriesAsync(
                team1.Concat(team2)
                    .Where(p => !string.IsNullOrEmpty(p.Puuid))
                    .DistinctBy(p => p.Puuid)
                    .Select(p => p.Puuid)
                    .ToList()).ConfigureAwait(false);
            return DetectCore(team1, team2, histories);
        }

        /// <summary>
        /// 纯算法入口：给定两支队伍与每人战绩摘要，计算开黑小组（便于单元验证）。
        /// </summary>
        public static PremadeResult DetectCore(
            List<(string Puuid, string Name)> team1,
            List<(string Puuid, string Name)> team2,
            IEnumerable<(string Puuid, GameHeadModel.MatchHistoryResponse? Data)> histories)
        {
            var result = new PremadeResult();
            var allPlayers = team1.Concat(team2)
                .Where(p => !string.IsNullOrEmpty(p.Puuid))
                .DistinctBy(p => p.Puuid)
                .ToList();
            if (allPlayers.Count < 2) return result;

            // puuid → 当前队伍索引（0=蓝方，1=红方）
            var teamOf = new Dictionary<string, int>();
            foreach (var p in team1.Where(p => !string.IsNullOrEmpty(p.Puuid)))
                teamOf[p.Puuid] = 0;
            foreach (var p in team2.Where(p => !string.IsNullOrEmpty(p.Puuid)))
                teamOf[p.Puuid] = 1;

            // 先把每个战绩摘要中可见的当前玩家合并到对应对局。
            // 同一局会出现在多名玩家的战绩里，而不同摘要返回的身份信息并不总是完整：
            // 不能在读到第一份摘要后就跳过该局，否则会漏掉另一组双排（例如 2+2）。
            var playersByGame = new Dictionary<long, Dictionary<string, int>>();
            foreach (var (_, history) in histories)
            {
                if (history?.Games?.Games == null) continue;
                foreach (var game in history.Games.Games)
                {
                    if (game.Participants == null || game.ParticipantIdentities == null) continue;
                    if (game.GameId <= 0) continue;

                    // 该局：puuid → teamId
                    var pidToTeam = game.Participants
                        .Where(p => p.TeamId > 0)
                        .ToDictionary(p => p.ParticipantId, p => p.TeamId);
                    if (!playersByGame.TryGetValue(game.GameId, out var playersInGame))
                    {
                        playersByGame[game.GameId] = playersInGame = new Dictionary<string, int>();
                    }

                    foreach (var identity in game.ParticipantIdentities)
                    {
                        var player = identity.Player;
                        if (player == null || string.IsNullOrEmpty(player.Puuid)) continue;
                        if (!teamOf.ContainsKey(player.Puuid)) continue; // 只看当前对局的玩家
                        if (!pidToTeam.ContainsKey(identity.ParticipantId)) continue;
                        playersInGame[player.Puuid] = pidToTeam[identity.ParticipantId];
                    }
                }
            }

            // 选取共同同队次数达标的完整小组，不能用关系链传递合并成三排或四排。
            var candidateGroups = new List<List<string>>();
            foreach (int side in new[] { 0, 1 })
            {
                string[] members = allPlayers.Where(player => teamOf[player.Puuid] == side)
                    .Select(player => player.Puuid).OrderBy(puuid => puuid, StringComparer.Ordinal).ToArray();
                if (members.Length > 10) continue;
                var remaining = members.ToHashSet(StringComparer.Ordinal);
                while (remaining.Count >= 2)
                {
                    List<string>? best = Enumerable.Range(1, (1 << members.Length) - 1)
                        .Select(mask => members.Where((_, index) => (mask & (1 << index)) != 0).ToList())
                        .Where(group => group.Count >= 2 && group.All(remaining.Contains))
                        .Where(group => playersByGame.Values.Count(game =>
                            game.TryGetValue(group[0], out int historicalTeam) &&
                            group.All(puuid => game.TryGetValue(puuid, out int otherTeam) && otherTeam == historicalTeam)) >= SameTeamThreshold)
                        .OrderByDescending(group => group.Count)
                        .ThenBy(group => string.Join(",", group), StringComparer.Ordinal).FirstOrDefault();
                    if (best == null) break;
                    candidateGroups.Add(best);
                    foreach (string puuid in best) remaining.Remove(puuid);
                }
            }

            int groupIndex = 0;
            foreach (var list in candidateGroups
                         .Where(g => g.Count >= 2)
                         .OrderBy(g => teamOf[g[0]])
                         .ThenBy(g => g[0], StringComparer.Ordinal))
            {
                var names = list
                    .Select(p => allPlayers.First(x => x.Puuid == p).Name)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();
                var info = new PremadeGroupInfo
                {
                    Index = ++groupIndex,
                    Puuids = list,
                    Names = names,
                    TeamIndex = teamOf[list[0]]
                };
                result.Groups.Add(info);
                foreach (var puuid in list)
                    result.GroupByPuuid[puuid] = info;
            }

            return result;
        }

        /// <summary>
        /// 并发获取多玩家战绩摘要（带 5 分钟缓存与并发限制）。
        /// </summary>
        private static async Task<(string Puuid, GameHeadModel.MatchHistoryResponse? Data)[]> FetchHistoriesAsync(
            List<string> puuids)
        {
            using var gate = new SemaphoreSlim(6, 6);
            var tasks = puuids.Select(async puuid =>
            {
                await gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (HistoryCache.TryGetValue(puuid, out var cached) &&
                        DateTime.Now - cached.Time < CacheTtl)
                    {
                        return (puuid, cached.Data);
                    }

                    var data = await Game_Api.GetUserGame(puuid, "0", (HistoryCount - 1).ToString()).ConfigureAwait(false);
                    HistoryCache[puuid] = (DateTime.Now, data);
                    return (puuid, data);
                }
                catch
                {
                    return (puuid, (GameHeadModel.MatchHistoryResponse?)null);
                }
                finally
                {
                    gate.Release();
                }
            }).ToList();

            return await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        /// <summary>
        /// 清空战绩缓存（对局结束或切换玩家时调用）。
        /// </summary>
        public static void ClearCache()
        {
            HistoryCache.Clear();
        }
    }
}