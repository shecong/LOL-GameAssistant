using System.Text;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

        public static class QueueTypes
        {
            public const string RANKED_SOLO_5x5 = "RANKED_SOLO_5x5";           // 单双排/单排
            public const string RANKED_FLEX_SR = "RANKED_FLEX_SR";             // 灵活组排
            public const string RANKED_TFT = "RANKED_TFT";                     // 云顶之弈
            public const string RANKED_TFT_TURBO = "RANKED_TFT_TURBO";         // 云顶快速模式
            public const string RANKED_TFT_DOUBLE_UP = "RANKED_TFT_DOUBLE_UP"; // 云顶双人模式
        }

        public static class TierLevels
        {
            public static readonly string[] Tiers = {
            "IRON", "BRONZE", "SILVER", "GOLD", "PLATINUM",
            "DIAMOND", "MASTER", "GRANDMASTER", "CHALLENGER"
        };

            public static readonly string[] Divisions = { "IV", "III", "II", "I" };
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

        private static string? Str(JObject obj, string key)
            => obj[key]?.Type == JTokenType.Null ? null : obj[key]?.ToString();

        private static int Int(JObject obj, string key)
            => obj[key]?.Value<int>() ?? 0;

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

        /// <summary>
        /// 生成“定级赛”展示文本。
        /// </summary>
        public static string GetPlacementText(RankedEntry? entry)
        {
            if (entry == null) return "-";
            if (!entry.IsProvisional) return "已完成";

            int remaining = entry.ProvisionalGamesRemaining;
            int threshold = entry.ProvisionalGameThreshold > 0
                ? entry.ProvisionalGameThreshold
                : 10;
            int played = Math.Max(0, threshold - remaining);
            return remaining > 0
                ? $"第 {played + 1}/{threshold} 场"
                : "已完成";
        }

        /// <summary>
        /// 生成“晋级赛”展示文本（如“进行中 1胜1负”、“非晋级赛”）。
        /// </summary>
        public static string GetPromotionText(RankedEntry? entry)
        {
            if (entry == null) return "-";
            if (string.IsNullOrEmpty(entry.MiniSeriesProgress)) return "非晋级赛";

            var progress = entry.MiniSeriesProgress.ToUpperInvariant();
            int wins = progress.Count(c => c == 'W');
            int losses = progress.Count(c => c == 'L');
            int total = progress.Length;
            int winsNeeded = Math.Max(1, (total + 1) / 2);
            return $"进行中 {wins}胜{losses}负（还需{winsNeeded - wins}胜）";
        }

        /// <summary>
        /// 生成“晋级赛局数”展示文本（当前系列赛已打局数，如“2/3”）。
        /// </summary>
        public static string GetPromotionGameCountText(RankedEntry? entry)
        {
            if (entry == null) return "-";
            if (string.IsNullOrEmpty(entry.MiniSeriesProgress)) return "0/0";

            var progress = entry.MiniSeriesProgress.ToUpperInvariant();
            int played = progress.Count(c => c is 'W' or 'L' or 'N');
            int total = Math.Max(played, progress.Length);
            return $"{played}/{total}";
        }

        /// <summary>
        /// 获取赛季结束时间文本；找不到指定队列时依次兜底单双排/任意赛季。
        /// </summary>
        public static string GetSeasonEndText(RankedData? data, string queueType)
        {
            if (data?.Seasons == null || data.Seasons.Count == 0) return "-";

            string[] keys =
            {
                queueType,
                QueueTypes.RANKED_SOLO_5x5,
                QueueTypes.RANKED_FLEX_SR
            };

            foreach (var key in keys)
            {
                var season = data.Seasons.FirstOrDefault(p =>
                    string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
                if (season != null && season.CurrentSeasonEnd > 0)
                    return season.SeasonEndDateTime.ToString("yyyy-MM-dd");
            }

            // 任意一个非空赛季兜底
            var fallback = data.Seasons.Values.FirstOrDefault(s => s.CurrentSeasonEnd > 0);
            return fallback != null ? fallback.SeasonEndDateTime.ToString("yyyy-MM-dd") : "-";
        }

        /// <summary>
        /// 隐藏分颜色段位的中文名。
        /// </summary>
        public static string GetRatedTierName(string ratedTier)
        {
            return ratedTier.ToUpperInvariant() switch
            {
                "GRAY" => "灰",
                "GREEN" => "绿",
                "BLUE" => "蓝",
                "PURPLE" => "紫",
                "ORANGE" => "橙",
                _ => ""
            };
        }

        /// <summary>
        /// 获取主要排位队列数据（单双排）
        /// </summary>
        /// <param name="rankedData">排位数据对象</param>
        /// <returns>单双排队列数据</returns>
        public RankedEntry? GetMainRankedData(RankedData? rankedData)
        {
            return GetQueueData(rankedData, QueueTypes.RANKED_SOLO_5x5);
        }

        /// <summary>
        /// 检查玩家是否已完成定位赛
        /// </summary>
        /// <param name="entry">排位条目</param>
        /// <returns>true表示已完成定位赛</returns>
        public bool IsPlacementCompleted(RankedEntry? entry)
        {
            return entry != null && !entry.IsProvisional && entry.ProvisionalGamesRemaining == 0;
        }

        /// <summary>
        /// 检查是否处于晋级赛中
        /// </summary>
        /// <param name="entry">排位条目</param>
        /// <returns>true表示正在进行晋级赛</returns>
        public bool IsInPromotionSeries(RankedEntry? entry)
        {
            return entry != null && !string.IsNullOrEmpty(entry.MiniSeriesProgress);
        }

        /// <summary>
        /// 获取晋级赛进度分析
        /// </summary>
        /// <param name="entry">排位条目</param>
        /// <returns>晋级赛进度信息</returns>
        public PromotionSeriesInfo? GetPromotionSeriesInfo(RankedEntry? entry)
        {
            if (entry == null || !IsInPromotionSeries(entry)) return null;

            var progress = entry.MiniSeriesProgress;
            int wins = progress.Count(c => c == 'W');
            int losses = progress.Count(c => c == 'L');
            int total = progress.Length;
            int needed = (int)Math.Ceiling(total / 2.0); // 需要赢得的场次

            return new PromotionSeriesInfo
            {
                Progress = progress,
                Wins = wins,
                Losses = losses,
                TotalGames = total,
                WinsNeeded = needed - wins,
                IsCompleted = wins >= needed || losses > total - needed
            };
        }

        /// <summary>
        /// 生成排位数据摘要
        /// </summary>
        /// <param name="rankedData">排位数据对象</param>
        /// <returns>格式化后的摘要信息</returns>
        public string GetRankedSummary(RankedData? rankedData)
        {
            var mainQueue = GetMainRankedData(rankedData);
            if (mainQueue == null) return "暂无排位数据";

            var sb = new StringBuilder();
            sb.AppendLine($"当前段位: {mainQueue.Tier} {mainQueue.Division}");
            sb.AppendLine($"胜点: {mainQueue.LeaguePoints} LP");
            sb.AppendLine($"战绩: {mainQueue.Wins}胜 {mainQueue.Losses}负 (胜率: {mainQueue.WinRate}%)");
            sb.AppendLine($"历史最高: {mainQueue.HighestTier} {mainQueue.HighestDivision}");

            if (IsInPromotionSeries(mainQueue))
            {
                var promoInfo = GetPromotionSeriesInfo(mainQueue);
                if (promoInfo != null)
                    sb.AppendLine($"晋级赛: {promoInfo.Progress} ({promoInfo.Wins}胜{promoInfo.Losses}负)");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 比较当前段位与历史最高段位
        /// </summary>
        /// <param name="entry">排位条目</param>
        /// <returns>段位比较结果</returns>
        public RankComparisonResult? CompareWithHighest(RankedEntry? entry)
        {
            if (entry == null) return null;

            int currentRank = GetRankValue(entry.Tier, entry.Division);
            int highestRank = GetRankValue(entry.HighestTier, entry.HighestDivision);

            return new RankComparisonResult
            {
                CurrentRankValue = currentRank,
                HighestRankValue = highestRank,
                Difference = highestRank - currentRank,
                IsAtPeak = currentRank >= highestRank
            };
        }

        /// <summary>
        /// 将段位转换为数值用于比较
        /// </summary>
        /// <param name="tier">段位</param>
        /// <param name="division">小段位</param>
        /// <returns>段位数值（越高表示段位越高）</returns>
        private int GetRankValue(string? tier, string? division)
        {
            if (string.IsNullOrEmpty(tier) || tier == "NONE") return 0;

            int tierIndex = Array.IndexOf(TierLevels.Tiers, tier);
            if (tierIndex < 0) return 0;

            // 大师及以上段位没有小段（division 为 NA/空），按 I 段处理，
            // 避免与钻石 I 并列。
            int divisionIndex = Array.IndexOf(TierLevels.Divisions, division);
            int divisionValue = divisionIndex >= 0 ? divisionIndex + 1 : 1;

            return tierIndex * 4 + divisionValue;
        }

        #endregion 核心分析方法

        #region 辅助类

        /// <summary>
        /// 晋级赛信息
        /// </summary>
        public class PromotionSeriesInfo
        {
            public string Progress { get; set; } = "";
            public int Wins { get; set; }
            public int Losses { get; set; }
            public int TotalGames { get; set; }
            public int WinsNeeded { get; set; }
            public bool IsCompleted { get; set; }
        }

        /// <summary>
        /// 段位比较结果
        /// </summary>
        public class RankComparisonResult
        {
            public int CurrentRankValue { get; set; }
            public int HighestRankValue { get; set; }
            public int Difference { get; set; }
            public bool IsAtPeak { get; set; }
        }

        #endregion 辅助类
    }
}
