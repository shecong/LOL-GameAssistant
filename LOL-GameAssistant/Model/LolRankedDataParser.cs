using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LOL_GameAssistant.Model
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
            [JsonPropertyName("currentSeasonSplitPoints")]
            public int CurrentSeasonSplitPoints { get; set; }

            /// <summary>已获得的荣誉奖励ID列表</summary>
            [JsonPropertyName("earnedRegaliaRewardIds")]
            public List<string> EarnedRegaliaRewardIds { get; set; } = new List<string>();

            /// <summary>当前赛季达到的最高段位</summary>
            [JsonPropertyName("highestCurrentSeasonReachedTierSR")]
            public string HighestCurrentSeasonReachedTierSR { get; set; }

            /// <summary>上赛季结束时的最高小段</summary>
            [JsonPropertyName("highestPreviousSeasonEndDivision")]
            public string HighestPreviousSeasonEndDivision { get; set; }

            /// <summary>上赛季结束时的最高段位</summary>
            [JsonPropertyName("highestPreviousSeasonEndTier")]
            public string HighestPreviousSeasonEndTier { get; set; }

            /// <summary>最高排位条目（主要队列）</summary>
            [JsonPropertyName("highestRankedEntry")]
            public RankedEntry HighestRankedEntry { get; set; }

            /// <summary>按队列类型映射的排位数据</summary>
            [JsonPropertyName("queueMap")]
            public Dictionary<string, RankedEntry> QueueMap { get; set; } = new Dictionary<string, RankedEntry>();

            /// <summary>所有队列的排位数据列表</summary>
            [JsonPropertyName("queues")]
            public List<RankedEntry> Queues { get; set; } = new List<RankedEntry>();

            /// <summary>荣誉等级</summary>
            [JsonPropertyName("rankedRegaliaLevel")]
            public int RankedRegaliaLevel { get; set; }

            /// <summary>各队列的赛季时间信息</summary>
            [JsonPropertyName("seasons")]
            public Dictionary<string, SeasonInfo> Seasons { get; set; } = new Dictionary<string, SeasonInfo>();

            /// <summary>分段进度</summary>
            [JsonPropertyName("splitsProgress")]
            public object SplitsProgress { get; set; }
        }

        /// <summary>
        /// 单排位队列数据
        /// </summary>
        public class RankedEntry
        {
            /// <summary>当前赛季胜利场次（用于奖励计算）</summary>
            [JsonPropertyName("currentSeasonWinsForRewards")]
            public int CurrentSeasonWinsForRewards { get; set; }

            /// <summary>当前小段位（I, II, III, IV, NA）</summary>
            [JsonPropertyName("division")]
            public string Division { get; set; }

            /// <summary>历史最高小段位</summary>
            [JsonPropertyName("highestDivision")]
            public string HighestDivision { get; set; }

            /// <summary>历史最高段位</summary>
            [JsonPropertyName("highestTier")]
            public string HighestTier { get; set; }

            /// <summary>是否处于定位赛阶段</summary>
            [JsonPropertyName("isProvisional")]
            public bool IsProvisional { get; set; }

            /// <summary>胜点（LP）0-100</summary>
            [JsonPropertyName("leaguePoints")]
            public int LeaguePoints { get; set; }

            /// <summary>失败场次</summary>
            [JsonPropertyName("losses")]
            public int Losses { get; set; }

            /// <summary>晋级赛进度（如"WLL"表示胜-负-负）</summary>
            [JsonPropertyName("miniSeriesProgress")]
            public string MiniSeriesProgress { get; set; }

            /// <summary>上赛季结束小段位</summary>
            [JsonPropertyName("previousSeasonEndDivision")]
            public string PreviousSeasonEndDivision { get; set; }

            /// <summary>上赛季结束段位</summary>
            [JsonPropertyName("previousSeasonEndTier")]
            public string PreviousSeasonEndTier { get; set; }

            /// <summary>上赛季最高小段位</summary>
            [JsonPropertyName("previousSeasonHighestDivision")]
            public string PreviousSeasonHighestDivision { get; set; }

            /// <summary>上赛季最高段位</summary>
            [JsonPropertyName("previousSeasonHighestTier")]
            public string PreviousSeasonHighestTier { get; set; }

            /// <summary>上赛季胜利场次（奖励用）</summary>
            [JsonPropertyName("previousSeasonWinsForRewards")]
            public int PreviousSeasonWinsForRewards { get; set; }

            /// <summary>定位赛场次阈值（通常为10场）</summary>
            [JsonPropertyName("provisionalGameThreshold")]
            public int ProvisionalGameThreshold { get; set; }

            /// <summary>剩余定位赛场次</summary>
            [JsonPropertyName("provisionalGamesRemaining")]
            public int ProvisionalGamesRemaining { get; set; }

            /// <summary>队列类型</summary>
            [JsonPropertyName("queueType")]
            public string QueueType { get; set; }

            /// <summary>隐藏分评分</summary>
            [JsonPropertyName("ratedRating")]
            public int RatedRating { get; set; }

            /// <summary>评分段位</summary>
            [JsonPropertyName("ratedTier")]
            public string RatedTier { get; set; }

            /// <summary>当前段位</summary>
            [JsonPropertyName("tier")]
            public string Tier { get; set; }

            /// <summary>警告信息</summary>
            [JsonPropertyName("warnings")]
            public object Warnings { get; set; }

            /// <summary>胜利场次</summary>
            [JsonPropertyName("wins")]
            public int Wins { get; set; }

            /// <summary>计算总场次</summary>
            [JsonIgnore]
            public int TotalGames => Wins + Losses;

            /// <summary>计算胜率</summary>
            [JsonIgnore]
            public double WinRate => TotalGames > 0 ? Math.Round((double)Wins / TotalGames * 100, 1) : 0;
        }

        /// <summary>
        /// 赛季时间信息
        /// </summary>
        public class SeasonInfo
        {
            /// <summary>当前赛季结束时间（Unix时间戳）</summary>
            [JsonPropertyName("currentSeasonEnd")]
            public long CurrentSeasonEnd { get; set; }

            /// <summary>当前赛季ID</summary>
            [JsonPropertyName("currentSeasonId")]
            public int CurrentSeasonId { get; set; }

            /// <summary>下赛季开始时间</summary>
            [JsonPropertyName("nextSeasonStart")]
            public long NextSeasonStart { get; set; }

            /// <summary>获取赛季结束时间（DateTime格式）</summary>
            [JsonIgnore]
            public DateTime SeasonEndDateTime => DateTimeOffset.FromUnixTimeMilliseconds(CurrentSeasonEnd).DateTime;
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
        public RankedData ParseRankedData(string jsonData)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = JsonNumberHandling.AllowReadingFromString
                };

                return JsonSerializer.Deserialize<RankedData>(jsonData, options);
            }
            catch (Exception ex)
            {
                throw new ArgumentException("排位数据解析失败", ex);
            }
        }

        /// <summary>
        /// 获取指定队列的排位数据
        /// </summary>
        /// <param name="rankedData">排位数据对象</param>
        /// <param name="queueType">队列类型</param>
        /// <returns>指定队列的排位条目，未找到返回null</returns>
        public RankedEntry GetQueueData(RankedData rankedData, string queueType)
        {
            if (rankedData?.QueueMap == null) return null;

            return rankedData.QueueMap.ContainsKey(queueType)
                ? rankedData.QueueMap[queueType]
                : null;
        }

        /// <summary>
        /// 获取主要排位队列数据（单双排）
        /// </summary>
        /// <param name="rankedData">排位数据对象</param>
        /// <returns>单双排队列数据</returns>
        public RankedEntry GetMainRankedData(RankedData rankedData)
        {
            return GetQueueData(rankedData, QueueTypes.RANKED_SOLO_5x5);
        }

        /// <summary>
        /// 检查玩家是否已完成定位赛
        /// </summary>
        /// <param name="entry">排位条目</param>
        /// <returns>true表示已完成定位赛</returns>
        public bool IsPlacementCompleted(RankedEntry entry)
        {
            return entry != null && !entry.IsProvisional && entry.ProvisionalGamesRemaining == 0;
        }

        /// <summary>
        /// 检查是否处于晋级赛中
        /// </summary>
        /// <param name="entry">排位条目</param>
        /// <returns>true表示正在进行晋级赛</returns>
        public bool IsInPromotionSeries(RankedEntry entry)
        {
            return entry != null && !string.IsNullOrEmpty(entry.MiniSeriesProgress);
        }

        /// <summary>
        /// 获取晋级赛进度分析
        /// </summary>
        /// <param name="entry">排位条目</param>
        /// <returns>晋级赛进度信息</returns>
        public PromotionSeriesInfo GetPromotionSeriesInfo(RankedEntry entry)
        {
            if (!IsInPromotionSeries(entry)) return null;

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
        public string GetRankedSummary(RankedData rankedData)
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
                sb.AppendLine($"晋级赛: {promoInfo.Progress} ({promoInfo.Wins}胜{promoInfo.Losses}负)");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 比较当前段位与历史最高段位
        /// </summary>
        /// <param name="entry">排位条目</param>
        /// <returns>段位比较结果</returns>
        public RankComparisonResult CompareWithHighest(RankedEntry entry)
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
        private int GetRankValue(string tier, string division)
        {
            if (string.IsNullOrEmpty(tier) || tier == "NONE") return 0;

            int tierValue = Array.IndexOf(TierLevels.Tiers, tier) * 4;
            int divisionValue = Array.IndexOf(TierLevels.Divisions, division) + 1;

            return tierValue + divisionValue;
        }

        #endregion 核心分析方法

        #region 辅助类

        /// <summary>
        /// 晋级赛信息
        /// </summary>
        public class PromotionSeriesInfo
        {
            public string Progress { get; set; }
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