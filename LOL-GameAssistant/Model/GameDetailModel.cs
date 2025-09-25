using LOL_GameAssistant.Models;
using System.Text.Json.Serialization;

namespace LOL_GameAssistant.Models
{
    public class GameDetailModel
    {
        /// <summary>
        /// 单场游戏完整信息（CHERRY模式）
        /// </summary>
        public class GameInfo
        {
            /// <summary>
            /// 游戏结果状态
            /// </summary>
            [JsonPropertyName("endOfGameResult")]
            public string EndOfGameResult { get; set; } = "GameComplete";

            /// <summary>
            /// 游戏创建时间戳（毫秒）
            /// </summary>
            [JsonPropertyName("gameCreation")]
            public long GameCreation { get; set; }

            /// <summary>
            /// ISO格式游戏创建时间
            /// </summary>
            [JsonPropertyName("gameCreationDate")]
            public DateTime GameCreationDate { get; set; }

            /// <summary>
            /// 游戏时长（秒）
            /// </summary>
            [JsonPropertyName("gameDuration")]
            public int GameDuration { get; set; }

            /// <summary>
            /// 游戏唯一ID
            /// </summary>
            [JsonPropertyName("gameId")]
            public long GameId { get; set; }

            /// <summary>
            /// 游戏模式（CHERRY=新模式）
            /// </summary>
            [JsonPropertyName("gameMode")]
            public string GameMode { get; set; } = "CHERRY";

            /// <summary>
            /// 游戏模式修饰器
            /// </summary>
            [JsonPropertyName("gameModeMutators")]
            public List<string> GameModeMutators { get; set; } = new List<string>();

            /// <summary>
            /// 游戏类型（MATCHED_GAME=匹配游戏）
            /// </summary>
            [JsonPropertyName("gameType")]
            public string GameType { get; set; } = "MATCHED_GAME";

            /// <summary>
            /// 游戏版本号
            /// </summary>
            [JsonPropertyName("gameVersion")]
            public string GameVersion { get; set; } = "15.18.710.6001";

            /// <summary>
            /// 地图ID（30=CHERRY模式特殊地图）
            /// </summary>
            [JsonPropertyName("mapId")]
            public int MapId { get; set; } = 30;

            /// <summary>
            /// 参与者身份信息（16名玩家）
            /// </summary>
            [JsonPropertyName("participantIdentities")]
            public List<ParticipantIdentity> ParticipantIdentities { get; set; } = new List<ParticipantIdentity>();

            /// <summary>
            /// 参与者游戏数据
            /// </summary>
            [JsonPropertyName("participants")]
            public List<Participant> Participants { get; set; } = new List<Participant>();

            /// <summary>
            /// 游戏平台ID（HN1=峡谷之巅）
            /// </summary>
            [JsonPropertyName("platformId")]
            public string PlatformId { get; set; } = "HN1";

            /// <summary>
            /// 队列ID（1700=CHERRY模式）
            /// </summary>
            [JsonPropertyName("queueId")]
            public int QueueId { get; set; } = 1700;

            /// <summary>
            /// 赛季ID
            /// </summary>
            [JsonPropertyName("seasonId")]
            public int SeasonId { get; set; }

            /// <summary>
            /// 队伍数据
            /// </summary>
            [JsonPropertyName("teams")]
            public List<Team> Teams { get; set; } = new List<Team>();
        }

        /// <summary>
        /// 参与者身份信息
        /// </summary>
        public class ParticipantIdentity
        {
            /// <summary>
            /// 参与者ID（1-16）
            /// </summary>
            [JsonPropertyName("participantId")]
            public int ParticipantId { get; set; }

            /// <summary>
            /// 玩家信息
            /// </summary>
            [JsonPropertyName("player")]
            public PlayerInfo Player { get; set; } = new PlayerInfo();
        }

        /// <summary>
        /// 玩家信息
        /// </summary>
        public class PlayerInfo
        {
            /// <summary>
            /// 账号ID
            /// </summary>
            [JsonPropertyName("accountId")]
            public long AccountId { get; set; }

            /// <summary>
            /// 当前账号ID
            /// </summary>
            [JsonPropertyName("currentAccountId")]
            public long CurrentAccountId { get; set; }

            /// <summary>
            /// 当前平台ID
            /// </summary>
            [JsonPropertyName("currentPlatformId")]
            public string CurrentPlatformId { get; set; } = "HN1";

            /// <summary>
            /// 游戏昵称（Riot ID名称）
            /// </summary>
            [JsonPropertyName("gameName")]
            public string GameName { get; set; } = string.Empty;

            /// <summary>
            /// 比赛历史URI
            /// </summary>
            [JsonPropertyName("matchHistoryUri")]
            public string MatchHistoryUri { get; set; } = string.Empty;

            /// <summary>
            /// 平台ID
            /// </summary>
            [JsonPropertyName("platformId")]
            public string PlatformId { get; set; } = "HN1";

            /// <summary>
            /// 头像ID
            /// </summary>
            [JsonPropertyName("profileIcon")]
            public int ProfileIcon { get; set; }

            /// <summary>
            /// 玩家全局唯一标识
            /// </summary>
            [JsonPropertyName("puuid")]
            public string Puuid { get; set; } = string.Empty;

            /// <summary>
            /// 召唤师ID
            /// </summary>
            [JsonPropertyName("summonerId")]
            public long SummonerId { get; set; }

            /// <summary>
            /// 召唤师名称（已废弃）
            /// </summary>
            [JsonPropertyName("summonerName")]
            public string SummonerName { get; set; } = string.Empty;

            /// <summary>
            /// 标签线（Riot ID标签）
            /// </summary>
            [JsonPropertyName("tagLine")]
            public string TagLine { get; set; } = string.Empty;
        }

        /// <summary>
        /// 参与者游戏数据
        /// </summary>
        public class Participant
        {
            /// <summary>
            /// 英雄ID
            /// </summary>
            [JsonPropertyName("championId")]
            public int ChampionId { get; set; }

            /// <summary>
            /// 达到的最高赛季段位
            /// </summary>
            [JsonPropertyName("highestAchievedSeasonTier")]
            public string HighestAchievedSeasonTier { get; set; } = string.Empty;

            /// <summary>
            /// 参与者ID
            /// </summary>
            [JsonPropertyName("participantId")]
            public int ParticipantId { get; set; }

            /// <summary>
            /// 召唤师技能1 ID（2201/2202=CHERRY模式特殊技能）
            /// </summary>
            [JsonPropertyName("spell1Id")]
            public int Spell1Id { get; set; }

            /// <summary>
            /// 召唤师技能2 ID
            /// </summary>
            [JsonPropertyName("spell2Id")]
            public int Spell2Id { get; set; }

            /// <summary>
            /// 游戏统计数据
            /// </summary>
            [JsonPropertyName("stats")]
            public ParticipantStats Stats { get; set; } = new ParticipantStats();

            /// <summary>
            /// 队伍ID（100=蓝色方，200=红色方）
            /// </summary>
            [JsonPropertyName("teamId")]
            public int TeamId { get; set; }

            /// <summary>
            /// 时间线数据
            /// </summary>
            [JsonPropertyName("timeline")]
            public ParticipantTimeline Timeline { get; set; } = new ParticipantTimeline();
        }

        /// <summary>
        /// 参与者游戏统计数据（CHERRY模式特殊字段）
        /// </summary>
        public class ParticipantStats
        {
            // 基础KDA数据
            [JsonPropertyName("kills")] public int Kills { get; set; }

            [JsonPropertyName("deaths")] public int Deaths { get; set; }
            [JsonPropertyName("assists")] public int Assists { get; set; }

            // 连杀数据
            [JsonPropertyName("doubleKills")] public int DoubleKills { get; set; }

            [JsonPropertyName("tripleKills")] public int TripleKills { get; set; }
            [JsonPropertyName("quadraKills")] public int QuadraKills { get; set; }
            [JsonPropertyName("pentaKills")] public int PentaKills { get; set; }
            [JsonPropertyName("killingSprees")] public int KillingSprees { get; set; }
            [JsonPropertyName("largestKillingSpree")] public int LargestKillingSpree { get; set; }
            [JsonPropertyName("largestMultiKill")] public int LargestMultiKill { get; set; }

            // 基础属性
            [JsonPropertyName("champLevel")] public int ChampLevel { get; set; }

            [JsonPropertyName("goldEarned")] public int GoldEarned { get; set; }
            [JsonPropertyName("goldSpent")] public int GoldSpent { get; set; }

            // CHERRY模式特殊字段 - Augment系统
            [JsonPropertyName("playerAugment1")] public int PlayerAugment1 { get; set; }

            [JsonPropertyName("playerAugment2")] public int PlayerAugment2 { get; set; }
            [JsonPropertyName("playerAugment3")] public int PlayerAugment3 { get; set; }
            [JsonPropertyName("playerAugment4")] public int PlayerAugment4 { get; set; }
            [JsonPropertyName("playerAugment5")] public int PlayerAugment5 { get; set; }
            [JsonPropertyName("playerAugment6")] public int PlayerAugment6 { get; set; }

            // CHERRY模式特殊字段 - 子队伍系统
            [JsonPropertyName("playerSubteamId")] public int PlayerSubteamId { get; set; }

            [JsonPropertyName("subteamPlacement")] public int SubteamPlacement { get; set; }

            // 装备信息（CHERRY模式特殊装备ID）
            [JsonPropertyName("item0")] public int Item0 { get; set; }

            [JsonPropertyName("item1")] public int Item1 { get; set; }
            [JsonPropertyName("item2")] public int Item2 { get; set; }
            [JsonPropertyName("item3")] public int Item3 { get; set; }
            [JsonPropertyName("item4")] public int Item4 { get; set; }
            [JsonPropertyName("item5")] public int Item5 { get; set; }
            [JsonPropertyName("item6")] public int Item6 { get; set; }

            // 伤害数据
            [JsonPropertyName("totalDamageDealtToChampions")] public int TotalDamageDealtToChampions { get; set; }

            [JsonPropertyName("magicDamageDealtToChampions")] public int MagicDamageDealtToChampions { get; set; }
            [JsonPropertyName("physicalDamageDealtToChampions")] public int PhysicalDamageDealtToChampions { get; set; }
            [JsonPropertyName("trueDamageDealtToChampions")] public int TrueDamageDealtToChampions { get; set; }

            // 承受伤害
            [JsonPropertyName("totalDamageTaken")] public int TotalDamageTaken { get; set; }

            [JsonPropertyName("damageSelfMitigated")] public int DamageSelfMitigated { get; set; }

            // 治疗和控制
            [JsonPropertyName("totalHeal")] public int TotalHeal { get; set; }

            [JsonPropertyName("timeCCingOthers")] public int TimeCCingOthers { get; set; }

            // 首杀/首塔等成就
            [JsonPropertyName("firstBloodKill")] public bool FirstBloodKill { get; set; }

            [JsonPropertyName("firstBloodAssist")] public bool FirstBloodAssist { get; set; }

            // 胜负结果
            [JsonPropertyName("win")] public bool Win { get; set; }

            // CHERRY模式无补刀数据（都为0）
            [JsonPropertyName("totalMinionsKilled")] public int TotalMinionsKilled { get; set; }

            [JsonPropertyName("neutralMinionsKilled")] public int NeutralMinionsKilled { get; set; }

            // CHERRY模式无视野数据（都为0）
            [JsonPropertyName("visionScore")] public int VisionScore { get; set; }

            [JsonPropertyName("wardsPlaced")] public int WardsPlaced { get; set; }
            [JsonPropertyName("wardsKilled")] public int WardsKilled { get; set; }

            // 符文数据（CHERRY模式中为0）
            [JsonPropertyName("perk0")] public int Perk0 { get; set; }

            [JsonPropertyName("perkPrimaryStyle")] public int PerkPrimaryStyle { get; set; }
            [JsonPropertyName("perkSubStyle")] public int PerkSubStyle { get; set; }
        }

        /// <summary>
        /// 参与者时间线数据
        /// </summary>
        public class ParticipantTimeline
        {
            /// <summary>
            /// 分路位置
            /// </summary>
            [JsonPropertyName("lane")]
            public string Lane { get; set; } = string.Empty;

            /// <summary>
            /// 参与者ID
            /// </summary>
            [JsonPropertyName("participantId")]
            public int ParticipantId { get; set; }

            /// <summary>
            /// 角色定位
            /// </summary>
            [JsonPropertyName("role")]
            public string Role { get; set; } = string.Empty;

            // CHERRY模式中时间线数据为空
            [JsonPropertyName("creepsPerMinDeltas")] public Dictionary<string, double> CreepsPerMinDeltas { get; set; } = new Dictionary<string, double>();

            [JsonPropertyName("goldPerMinDeltas")] public Dictionary<string, double> GoldPerMinDeltas { get; set; } = new Dictionary<string, double>();
        }

        /// <summary>
        /// 队伍数据
        /// </summary>
        public class Team
        {
            /// <summary>
            /// Ban选信息（CHERRY模式有16个Ban位）
            /// </summary>
            [JsonPropertyName("bans")]
            public List<BanInfo> Bans { get; set; } = new List<BanInfo>();

            /// <summary>
            /// 队伍ID（100=蓝色方，200=红色方，0=未知）
            /// </summary>
            [JsonPropertyName("teamId")]
            public int TeamId { get; set; }

            /// <summary>
            /// 胜负结果（Win/Fail）
            /// </summary>
            [JsonPropertyName("win")]
            public string Win { get; set; } = string.Empty;

            // CHERRY模式中无传统资源数据
            [JsonPropertyName("baronKills")] public int BaronKills { get; set; }

            [JsonPropertyName("dragonKills")] public int DragonKills { get; set; }
            [JsonPropertyName("towerKills")] public int TowerKills { get; set; }
            [JsonPropertyName("inhibitorKills")] public int InhibitorKills { get; set; }

            // 首杀/首塔等成就
            [JsonPropertyName("firstBlood")] public bool FirstBlood { get; set; }

            [JsonPropertyName("firstTower")] public bool FirstTower { get; set; }
            [JsonPropertyName("firstInhibitor")] public bool FirstInhibitor { get; set; }
        }

        /// <summary>
        /// Ban选信息
        /// </summary>
        public class BanInfo
        {
            /// <summary>
            /// 英雄ID（-1=空Ban）
            /// </summary>
            [JsonPropertyName("championId")]
            public int ChampionId { get; set; }

            /// <summary>
            /// Ban选顺序
            /// </summary>
            [JsonPropertyName("pickTurn")]
            public int PickTurn { get; set; }
        }
    }

    /// <summary>
    /// CHERRY模式数据分析工具类
    /// </summary>
    public static class CherryMatchAnalyzer
    {
        /// <summary>
        /// 计算KDA比率
        /// </summary>
        public static double CalculateKda(GameDetailModel.ParticipantStats stats)
        {
            return stats.Deaths == 0 ? stats.Kills + stats.Assists :
                   (stats.Kills + stats.Assists) / (double)stats.Deaths;
        }

        /// <summary>
        /// 获取玩家完整Riot ID
        /// </summary>
        public static string GetFullRiotId(GameDetailModel.PlayerInfo player)
        {
            return $"{player.GameName}#{player.TagLine}";
        }

        /// <summary>
        /// 分析Augment组合
        /// </summary>
        public static List<int> GetAugments(GameDetailModel.ParticipantStats stats)
        {
            var augments = new List<int>();
            if (stats.PlayerAugment1 > 0) augments.Add(stats.PlayerAugment1);
            if (stats.PlayerAugment2 > 0) augments.Add(stats.PlayerAugment2);
            if (stats.PlayerAugment3 > 0) augments.Add(stats.PlayerAugment3);
            if (stats.PlayerAugment4 > 0) augments.Add(stats.PlayerAugment4);
            return augments;
        }

        /// <summary>
        /// CHERRY模式专属评分
        /// </summary>
        public static double CalculateCherryScore(GameDetailModel.ParticipantStats stats)
        {
            var killScore = stats.Kills * 3;
            var assistScore = stats.Assists * 2;
            var deathPenalty = stats.Deaths * 1.5;
            var damageScore = stats.TotalDamageDealtToChampions / 1000.0;

            return killScore + assistScore - deathPenalty + damageScore;
        }
    }
}