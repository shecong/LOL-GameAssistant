using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;

namespace LOL_GameAssistant.Entity
{
    /// <summary>
    /// 英雄联盟排位赛数据解析类
    /// </summary>
    public class LolRankedDataParser
    {
        #region 数据模型类

        /// <summary>
        /// 排位赛数据主类
        /// </summary>
        public class RankedData
        {
            /// <summary>当前赛季分段点数</summary>
            [Newtonsoft.Json.JsonProperty("currentSeasonSplitPoints")]
            public int CurrentSeasonSplitPoints { get; set; }

            /// <summary>已获得的荣誉奖励ID列表</summary>
            [Newtonsoft.Json.JsonProperty("earnedRegaliaRewardIds")]
            public List<string> EarnedRegaliaRewardIds { get; set; } = new List<string>();

            /// <summary>当前赛季达到的最高段位</summary>
            [Newtonsoft.Json.JsonProperty("highestCurrentSeasonReachedTierSR")]
            public string HighestCurrentSeasonReachedTierSR { get; set; } = "";

            /// <summary>上赛季结束时的最高小段</summary>
            [Newtonsoft.Json.JsonProperty("highestPreviousSeasonEndDivision")]
            public string HighestPreviousSeasonEndDivision { get; set; } = "";

            /// <summary>上赛季结束时的最高段位</summary>
            [Newtonsoft.Json.JsonProperty("highestPreviousSeasonEndTier")]
            public string HighestPreviousSeasonEndTier { get; set; } = "";

            /// <summary>最高排位条目（主要队列）</summary>
            [Newtonsoft.Json.JsonProperty("highestRankedEntry")]
            public RankedEntry HighestRankedEntry { get; set; } = null!;

            /// <summary>最高排位条目（单双排专用）</summary>
            [Newtonsoft.Json.JsonProperty("highestRankedEntrySR")]
            public RankedEntry HighestRankedEntrySR { get; set; } = null!;

            /// <summary>按队列类型映射的排位数据</summary>
            [Newtonsoft.Json.JsonProperty("queueMap")]
            public Dictionary<string, RankedEntry> QueueMap { get; set; } = new Dictionary<string, RankedEntry>();

            /// <summary>所有队列的排位数据列表</summary>
            [Newtonsoft.Json.JsonProperty("queues")]
            public List<RankedEntry> Queues { get; set; } = new List<RankedEntry>();

            /// <summary>荣誉等级</summary>
            [Newtonsoft.Json.JsonProperty("rankedRegaliaLevel")]
            public int RankedRegaliaLevel { get; set; }

            /// <summary>各队列的赛季时间信息</summary>
            [Newtonsoft.Json.JsonProperty("seasons")]
            public Dictionary<string, SeasonInfo> Seasons { get; set; } = new Dictionary<string, SeasonInfo>();

            /// <summary>分段进度</summary>
            [Newtonsoft.Json.JsonProperty("splitsProgress")]
            public object SplitsProgress { get; set; } = new();
        }

        /// <summary>
        /// 单排位队列数据
        /// </summary>
        public class RankedEntry
        {
            /// <summary>当前赛季胜利场次（用于奖励计算）</summary>
            [Newtonsoft.Json.JsonProperty("currentSeasonWinsForRewards")]
            public int CurrentSeasonWinsForRewards { get; set; }

            /// <summary>当前小段位（I, II, III, IV, NA）</summary>
            [Newtonsoft.Json.JsonProperty("division")]
            public string Division { get; set; } = "";

            /// <summary>历史最高小段位</summary>
            [Newtonsoft.Json.JsonProperty("highestDivision")]
            public string HighestDivision { get; set; } = "";

            /// <summary>历史最高段位</summary>
            [Newtonsoft.Json.JsonProperty("highestTier")]
            public string HighestTier { get; set; } = "";

            /// <summary>是否处于定位赛阶段</summary>
            [Newtonsoft.Json.JsonProperty("isProvisional")]
            public bool IsProvisional { get; set; }

            /// <summary>胜点（LP）0-100</summary>
            [Newtonsoft.Json.JsonProperty("leaguePoints")]
            public int LeaguePoints { get; set; }

            /// <summary>失败场次</summary>
            [Newtonsoft.Json.JsonProperty("losses")]
            public int Losses { get; set; }

            /// <summary>晋级赛进度（如"WLL"表示胜-负-负）</summary>
            [Newtonsoft.Json.JsonProperty("miniSeriesProgress")]
            public string MiniSeriesProgress { get; set; } = "";

            /// <summary>上赛季结束小段位</summary>
            [Newtonsoft.Json.JsonProperty("previousSeasonEndDivision")]
            public string PreviousSeasonEndDivision { get; set; } = "";

            /// <summary>上赛季结束段位</summary>
            [Newtonsoft.Json.JsonProperty("previousSeasonEndTier")]
            public string PreviousSeasonEndTier { get; set; } = "";

            /// <summary>上赛季最高小段位</summary>
            [Newtonsoft.Json.JsonProperty("previousSeasonHighestDivision")]
            public string PreviousSeasonHighestDivision { get; set; } = "";

            /// <summary>上赛季最高段位</summary>
            [Newtonsoft.Json.JsonProperty("previousSeasonHighestTier")]
            public string PreviousSeasonHighestTier { get; set; } = "";

            /// <summary>上赛季胜利场次（奖励用）</summary>
            [Newtonsoft.Json.JsonProperty("previousSeasonWinsForRewards")]
            public int PreviousSeasonWinsForRewards { get; set; }

            /// <summary>定位赛场次阈值（通常为10场）</summary>
            [Newtonsoft.Json.JsonProperty("provisionalGameThreshold")]
            public int ProvisionalGameThreshold { get; set; }

            /// <summary>剩余定位赛场次</summary>
            [Newtonsoft.Json.JsonProperty("provisionalGamesRemaining")]
            public int ProvisionalGamesRemaining { get; set; }

            /// <summary>队列类型</summary>
            [Newtonsoft.Json.JsonProperty("queueType")]
            public string QueueType { get; set; } = "";

            /// <summary>隐藏分评分</summary>
            [Newtonsoft.Json.JsonProperty("ratedRating")]
            public int RatedRating { get; set; }

            /// <summary>评分段位</summary>
            [Newtonsoft.Json.JsonProperty("ratedTier")]
            public string RatedTier { get; set; } = "";

            /// <summary>当前段位</summary>
            [Newtonsoft.Json.JsonProperty("tier")]
            public string Tier { get; set; } = "";

            /// <summary>警告信息</summary>
            [Newtonsoft.Json.JsonProperty("warnings")]
            public object Warnings { get; set; } = new();

            /// <summary>胜利场次</summary>
            [Newtonsoft.Json.JsonProperty("wins")]
            public int Wins { get; set; }

            /// <summary>计算总场次</summary>
            [Newtonsoft.Json.JsonIgnore]
            public int TotalGames => Wins + Losses;

            /// <summary>计算胜率</summary>
            [Newtonsoft.Json.JsonIgnore]
            public double WinRate => TotalGames > 0 ? Math.Round((double)Wins / TotalGames * 100, 1) : 0;
        }

        /// <summary>
        /// 赛季时间信息
        /// </summary>
        public class SeasonInfo
        {
            /// <summary>当前赛季结束时间（Unix时间戳）</summary>
            [Newtonsoft.Json.JsonProperty("currentSeasonEnd")]
            public long CurrentSeasonEnd { get; set; }

            /// <summary>当前赛季ID</summary>
            [Newtonsoft.Json.JsonProperty("currentSeasonId")]
            public int CurrentSeasonId { get; set; }

            /// <summary>下赛季开始时间</summary>
            [Newtonsoft.Json.JsonProperty("nextSeasonStart")]
            public long NextSeasonStart { get; set; }

            /// <summary>获取赛季结束时间（DateTime格式）</summary>
            [Newtonsoft.Json.JsonIgnore]
            public DateTime SeasonEndDateTime
            {
                get
                {
                    // 兼容秒级与毫秒级时间戳
                    long ms = CurrentSeasonEnd;
                    if (ms > 0 && ms < 10_000_000_000L) ms *= 1000;
                    return DateTimeOffset.FromUnixTimeMilliseconds(ms).DateTime;
                }
            }
        }

        #endregion 数据模型类

        #region 队列类型常量

        /// <summary>排位数据中各队列的统计集合。</summary>
        public static class QueueTypes
        {
            public const string RANKED_SOLO_5x5 = "RANKED_SOLO_5x5";           // 单双排/单排
            public const string RANKED_FLEX_SR = "RANKED_FLEX_SR";             // 灵活组排
            public const string RANKED_TFT = "RANKED_TFT";                     // 云顶之弈
            public const string RANKED_TFT_TURBO = "RANKED_TFT_TURBO";         // 云顶快速模式
            public const string RANKED_TFT_DOUBLE_UP = "RANKED_TFT_DOUBLE_UP"; // 云顶双人模式
        }

        #endregion 队列类型常量

        #region 核心分析方法

        /// <summary>
        /// 解析排位数据JSON字符串
        /// </summary>
        /// <param name="jsonData">JSON格式的排位数据</param>
        /// <returns>解析后的排位数据对象</returns>
        public RankedData? ParseRankedData(string? jsonData)
        {
            if (string.IsNullOrWhiteSpace(jsonData)) return null;

            try
            {
                // 先做字段别名归一化（rank → division 等），再类型化解析，
                // 保证 typed 路径与手动兜底路径行为一致。
                var normalizedJson = NormalizeFieldAliases(jsonData);
                var typed = Newtonsoft.Json.JsonConvert.DeserializeObject<RankedData>(normalizedJson);
                if (typed != null && (typed.QueueMap?.Count > 0 || typed.Queues?.Count > 0))
                {
                    NormalizeMaps(typed);
                    ResolveHighestRanks(typed);
                    return typed;
                }
            }
            catch
            {
                // 类型化解析失败时走手动兜底
            }

            var manual = ParseRankedDataManual(NormalizeFieldAliases(jsonData));
            if (manual != null)
            {
                NormalizeMaps(manual);
                ResolveHighestRanks(manual);
            }
            return manual;
        }

        /// <summary>
        /// 客户端不同版本字段名有差异：新版本部分客户端用 rank 表示小段位，
        /// 这里统一复制为 division，同时保留原始字段。
        /// </summary>
        private static string NormalizeFieldAliases(string jsonData)
        {
            try
            {
                var root = JObject.Parse(jsonData);

                // 规范化单条排位数据的缺失值和字段表示。
                void NormalizeEntry(JObject? entry)
                {
                    if (entry == null) return;
                    if (entry["division"] == null && entry["rank"] != null)
                    {
                        entry["division"] = entry["rank"];
                    }
                }

                if (root["queueMap"] is JObject queueMap)
                {
                    foreach (var prop in queueMap.Properties())
                        NormalizeEntry(prop.Value as JObject);
                }

                if (root["queues"] is JArray queues)
                {
                    foreach (var token in queues)
                        NormalizeEntry(token as JObject);
                }

                NormalizeEntry(root["highestRankedEntry"] as JObject);
                NormalizeEntry(root["highestRankedEntrySR"] as JObject);

                return root.ToString(Formatting.None);
            }
            catch
            {
                return jsonData;
            }
        }

        /// <summary>
        /// 保证 queueMap 与 queues 两个视图一致：
        /// 客户端不同版本可能只返回其中一个，统一补齐后后续查询不再区分来源。
        /// </summary>
        private static void NormalizeMaps(RankedData data)
        {
            if (data == null) return;

            // queues 数组 → queueMap（若 queueMap 缺失）
            if (data.QueueMap.Count == 0 && data.Queues.Count > 0)
            {
                foreach (var entry in data.Queues)
                {
                    if (string.IsNullOrEmpty(entry.QueueType)) continue;
                    data.QueueMap.TryAdd(entry.QueueType, entry);
                }
            }

            // queueMap → queues 数组（若 queues 缺失）
            if (data.Queues.Count == 0 && data.QueueMap.Count > 0)
            {
                foreach (var pair in data.QueueMap)
                {
                    pair.Value.QueueType = pair.Key;
                    data.Queues.Add(pair.Value);
                }
            }
        }

        /// <summary>
        /// 将顶层最高段位（highestRankedEntry / highestRankedEntrySR）合并到各队列条目，
        /// 因为 LCU 新版本中队列条目本身不再带 highestTier / highestDivision。
        /// </summary>
        private static void ResolveHighestRanks(RankedData data)
        {
            if (data == null) return;

            var candidates = new List<RankedEntry>();
            if (data.HighestRankedEntry != null) candidates.Add(data.HighestRankedEntry);
            if (data.HighestRankedEntrySR != null) candidates.Add(data.HighestRankedEntrySR);

            foreach (var entry in data.Queues)
            {
                if (entry == null) continue;

                // 条目自带最高段位时优先保留
                if (!string.IsNullOrEmpty(entry.HighestTier) && entry.HighestTier != "NONE")
                    continue;

                // 优先取同一队列的顶层最高段位，其次取任意一个非空最高段位
                var matched = candidates.FirstOrDefault(c =>
                    c != null && string.Equals(c.QueueType, entry.QueueType, StringComparison.OrdinalIgnoreCase));
                matched ??= candidates.FirstOrDefault(c =>
                    c != null && !string.IsNullOrEmpty(c.Tier) && c.Tier != "NONE");

                if (matched != null)
                {
                    if (string.IsNullOrEmpty(entry.HighestTier) || entry.HighestTier == "NONE")
                    {
                        entry.HighestTier = matched.Tier;
                        entry.HighestDivision = matched.Division;
                    }
                }
            }
        }

        /// <summary>
        /// 手动兜底解析：兼容 queueMap 对象 / queues 数组 / 字段别名等不同版本结构。
        /// </summary>
        private RankedData? ParseRankedDataManual(string jsonData)
        {
            try
            {
                var root = JObject.Parse(jsonData);
                var data = new RankedData();

                if (root["seasons"] is JObject seasons)
                {
                    foreach (var prop in seasons.Properties())
                    {
                        data.Seasons[prop.Name] = prop.Value.ToObject<SeasonInfo>() ?? new SeasonInfo();
                    }
                }

                // 方式一：queueMap 对象 { "RANKED_SOLO_5x5": { ... } }
                if (root["queueMap"] is JObject queueMap)
                {
                    foreach (var prop in queueMap.Properties())
                    {
                        var entry = MapEntry(prop.Value as JObject);
                        if (entry == null) continue;
                        entry.QueueType = prop.Name;
                        data.QueueMap[prop.Name] = entry;
                        data.Queues.Add(entry);
                    }
                }

                // 方式二：queues 数组 [ { "queueType": "...", ... } ]
                if (root["queues"] is JArray queues)
                {
                    foreach (var token in queues)
                    {
                        var entry = MapEntry(token as JObject);
                        if (entry == null || string.IsNullOrEmpty(entry.QueueType)) continue;
                        data.QueueMap.TryAdd(entry.QueueType, entry);
                        if (data.Queues.All(q => q.QueueType != entry.QueueType))
                            data.Queues.Add(entry);
                    }
                }

                return data.QueueMap.Count > 0 ? data : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 将单个队列 JSON 节点映射为排位条目（字段缺失时使用默认值）。
        /// </summary>
        private static RankedEntry? MapEntry(JObject? obj)
        {
            if (obj == null) return null;

            // 新版本 LCU 部分客户端返回 rank，旧版本返回 division，两者都兼容
            string division = Str(obj, "division") ?? "";
            if (string.IsNullOrEmpty(division))
                division = Str(obj, "rank") ?? "";

            return new RankedEntry
            {
                QueueType = Str(obj, "queueType") ?? "",
                Tier = Str(obj, "tier") ?? "",
                Division = division,
                LeaguePoints = Int(obj, "leaguePoints"),
                Wins = Int(obj, "wins"),
                Losses = Int(obj, "losses"),
                CurrentSeasonWinsForRewards = Int(obj, "currentSeasonWinsForRewards"),
                PreviousSeasonWinsForRewards = Int(obj, "previousSeasonWinsForRewards"),
                IsProvisional = Bool(obj, "isProvisional"),
                ProvisionalGamesRemaining = Int(obj, "provisionalGamesRemaining"),
                ProvisionalGameThreshold = Int(obj, "provisionalGameThreshold"),
                MiniSeriesProgress = Str(obj, "miniSeriesProgress") ?? "",
                RatedRating = Int(obj, "ratedRating"),
                RatedTier = Str(obj, "ratedTier") ?? "",
                HighestTier = Str(obj, "highestTier") ?? "",
                HighestDivision = Str(obj, "highestDivision") ?? "",
                PreviousSeasonEndTier = Str(obj, "previousSeasonEndTier") ?? "",
                PreviousSeasonEndDivision = Str(obj, "previousSeasonEndDivision") ?? "",
                PreviousSeasonHighestTier = Str(obj, "previousSeasonHighestTier") ?? "",
                PreviousSeasonHighestDivision = Str(obj, "previousSeasonHighestDivision") ?? ""
            };
        }

        /// <summary>从 JSON 字段读取字符串值。</summary>
        private static string? Str(JObject obj, string key)
            => obj[key]?.Type == JTokenType.Null ? null : obj[key]?.ToString();

        /// <summary>从 JSON 字段读取整数值。</summary>
        private static int Int(JObject obj, string key)
            => obj[key]?.Value<int>() ?? 0;

        /// <summary>从 JSON 字段读取布尔值。</summary>
        private static bool Bool(JObject obj, string key)
            => obj[key]?.Value<bool>() ?? false;

        /// <summary>
        /// 获取指定队列的排位数据
        /// </summary>
        /// <param name="rankedData">排位数据对象</param>
        /// <param name="queueType">队列类型</param>
        /// <returns>指定队列的排位条目，未找到返回null</returns>
        public RankedEntry? GetQueueData(RankedData? rankedData, string queueType)
        {
            if (rankedData?.QueueMap == null) return null;

            // 优先精确匹配
            if (rankedData.QueueMap.TryGetValue(queueType, out var entry))
                return entry;

            // 兼容大小写差异（如 RANKED_SOLO_5X5）
            var fuzzy = rankedData.QueueMap.FirstOrDefault(p =>
                string.Equals(p.Key, queueType, StringComparison.OrdinalIgnoreCase));
            if (fuzzy.Key != null)
                return fuzzy.Value;

            // 客户端只返回 queues 数组时兜底
            if (rankedData.Queues != null)
            {
                var queueEntry = rankedData.Queues.FirstOrDefault(q =>
                    string.Equals(q.QueueType, queueType, StringComparison.OrdinalIgnoreCase));
                if (queueEntry != null) return queueEntry;
            }

            return null;
        }

        #endregion 辅助类
    }
}
