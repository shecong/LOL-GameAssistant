using System.Text.Json.Serialization;

namespace LOL_GameAssistant.Entity
{
    public class GameHeadModel
    {
        /// <summary>
        /// 比赛历史响应数据
        /// </summary>
        public class MatchHistoryResponse
        {
            /// <summary>
            /// 账号ID
            /// </summary>
            [JsonPropertyName("accountId")]
            public long AccountId { get; set; }

            /// <summary>
            /// 游戏数据容器
            /// </summary>
            [JsonPropertyName("games")]
            public GamesContainer? Games { get; set; }

            /// <summary>
            /// 游戏平台ID（如：HN1=峡谷之巅）
            /// </summary>
            [JsonPropertyName("platformId")]
            public string? PlatformId { get; set; }
        }

        /// <summary>
        /// 游戏数据容器
        /// </summary>
        public class GamesContainer
        {
            /// <summary>
            /// 游戏开始日期范围（空表示无限制）
            /// </summary>
            [JsonPropertyName("gameBeginDate")]
            public string? GameBeginDate { get; set; }

            /// <summary>
            /// 总游戏场数
            /// </summary>
            [JsonPropertyName("gameCount")]
            public int GameCount { get; set; }

            /// <summary>
            /// 游戏结束日期范围
            /// </summary>
            [JsonPropertyName("gameEndDate")]
            public string? GameEndDate { get; set; }

            /// <summary>
            /// 当前返回的游戏起始索引
            /// </summary>
            [JsonPropertyName("gameIndexBegin")]
            public int GameIndexBegin { get; set; }

            /// <summary>
            /// 当前返回的游戏结束索引
            /// </summary>
            [JsonPropertyName("gameIndexEnd")]
            public int GameIndexEnd { get; set; }

            /// <summary>
            /// 具体的游戏数据数组
            /// </summary>
            [JsonPropertyName("games")]
            public List<GameInfo>? Games { get; set; }
        }

        /// <summary>
        /// 单场游戏信息
        /// </summary>
        public class GameInfo
        {
            /// <summary>
            /// 游戏结果状态（GameComplete=游戏完成）
            /// </summary>
            [JsonPropertyName("endOfGameResult")]
            public string? EndOfGameResult { get; set; }

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
            /// 游戏模式（CLASSIC=经典，RUBY_TRIAL_1=红宝石试炼，CHERRY=新模式）
            /// </summary>
            [JsonPropertyName("gameMode")]
            public string? GameMode { get; set; }

            /// <summary>
            /// 游戏模式修饰器
            /// </summary>
            [JsonPropertyName("gameModeMutators")]
            public List<string>? GameModeMutators { get; set; }

            /// <summary>
            /// 游戏类型（MATCHED_GAME=匹配游戏）
            /// </summary>
            [JsonPropertyName("gameType")]
            public string? GameType { get; set; }

            /// <summary>
            /// 游戏版本号
            /// </summary>
            [JsonPropertyName("gameVersion")]
            public string? GameVersion { get; set; }

            /// <summary>
            /// 地图ID（11=召唤师峡谷，30=其他地图）
            /// </summary>
            [JsonPropertyName("mapId")]
            public int MapId { get; set; }

            /// <summary>
            /// 参与者身份信息
            /// </summary>
            [JsonPropertyName("participantIdentities")]
            public List<ParticipantIdentity>? ParticipantIdentities { get; set; }

            /// <summary>
            /// 参与者游戏数据
            /// </summary>
            [JsonPropertyName("participants")]
            public List<Participant>? Participants { get; set; }

            /// <summary>
            /// 游戏平台ID
            /// </summary>
            [JsonPropertyName("platformId")]
            public string? PlatformId { get; set; }

            /// <summary>
            /// 队列ID（420=单排/双排，1700/4240=特殊模式）
            /// </summary>
            [JsonPropertyName("queueId")]
            public int QueueId { get; set; }

            /// <summary>
            /// 赛季ID
            /// </summary>
            [JsonPropertyName("seasonId")]
            public int SeasonId { get; set; }

            /// <summary>
            /// 队伍数据
            /// </summary>
            [JsonPropertyName("teams")]
            public List<Team>? Teams { get; set; }
        }

        /// <summary>
        /// 参与者身份信息
        /// </summary>
        public class ParticipantIdentity
        {
            /// <summary>
            /// 参与者ID（1-10）
            /// </summary>
            [JsonPropertyName("participantId")]
            public int ParticipantId { get; set; }

            /// <summary>
            /// 玩家信息
            /// </summary>
            [JsonPropertyName("player")]
            public PlayerInfo? Player { get; set; }
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
            public string? CurrentPlatformId { get; set; }

            /// <summary>
            /// 游戏昵称
            /// </summary>
            [JsonPropertyName("gameName")]
            public string? GameName { get; set; }

            /// <summary>
            /// 比赛历史URI
            /// </summary>
            [JsonPropertyName("matchHistoryUri")]
            public string? MatchHistoryUri { get; set; }

            /// <summary>
            /// 平台ID
            /// </summary>
            [JsonPropertyName("platformId")]
            public string? PlatformId { get; set; }

            /// <summary>
            /// 头像ID
            /// </summary>
            [JsonPropertyName("profileIcon")]
            public int ProfileIcon { get; set; }

            /// <summary>
            /// 玩家唯一标识
            /// </summary>
            [JsonPropertyName("puuid")]
            public string? Puuid { get; set; }

            /// <summary>
            /// 召唤师ID
            /// </summary>
            [JsonPropertyName("summonerId")]
            public long SummonerId { get; set; }

            /// <summary>
            /// 召唤师名称（已废弃，使用GameName和TagLine）
            /// </summary>
            [JsonPropertyName("summonerName")]
            public string? SummonerName { get; set; }

            /// <summary>
            /// 标签线（如#42483）
            /// </summary>
            [JsonPropertyName("tagLine")]
            public string? TagLine { get; set; }
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
            public string? HighestAchievedSeasonTier { get; set; }

            /// <summary>
            /// 参与者ID
            /// </summary>
            [JsonPropertyName("participantId")]
            public int ParticipantId { get; set; }

            /// <summary>
            /// 召唤师技能1 ID
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
            public ParticipantStats? Stats { get; set; }

            /// <summary>
            /// 队伍ID（100=蓝色方，200=红色方）
            /// </summary>
            [JsonPropertyName("teamId")]
            public int TeamId { get; set; }

            /// <summary>
            /// 时间线数据
            /// </summary>
            [JsonPropertyName("timeline")]
            public ParticipantTimeline? Timeline { get; set; }
        }

        /// <summary>
        /// 参与者游戏统计数据
        /// </summary>
        public class ParticipantStats
        {
            // KDA相关
            [JsonPropertyName("assists")] public int Assists { get; set; }

            [JsonPropertyName("deaths")] public int Deaths { get; set; }
            [JsonPropertyName("kills")] public int Kills { get; set; }

            // 连杀数据
            [JsonPropertyName("doubleKills")] public int DoubleKills { get; set; }

            [JsonPropertyName("tripleKills")] public int TripleKills { get; set; }
            [JsonPropertyName("quadraKills")] public int QuadraKills { get; set; }
            [JsonPropertyName("pentaKills")] public int PentaKills { get; set; }
            [JsonPropertyName("killingSprees")] public int KillingSprees { get; set; }
            [JsonPropertyName("largestKillingSpree")] public int LargestKillingSpree { get; set; }
            [JsonPropertyName("largestMultiKill")] public int LargestMultiKill { get; set; }

            // 基础数据
            [JsonPropertyName("champLevel")] public int ChampLevel { get; set; }

            [JsonPropertyName("goldEarned")] public int GoldEarned { get; set; }
            [JsonPropertyName("goldSpent")] public int GoldSpent { get; set; }

            // 补刀数据
            [JsonPropertyName("totalMinionsKilled")] public int TotalMinionsKilled { get; set; }

            [JsonPropertyName("neutralMinionsKilled")] public int NeutralMinionsKilled { get; set; }
            [JsonPropertyName("neutralMinionsKilledEnemyJungle")] public int NeutralMinionsKilledEnemyJungle { get; set; }
            [JsonPropertyName("neutralMinionsKilledTeamJungle")] public int NeutralMinionsKilledTeamJungle { get; set; }

            // 视野数据
            [JsonPropertyName("visionScore")] public int VisionScore { get; set; }

            [JsonPropertyName("wardsPlaced")] public int WardsPlaced { get; set; }
            [JsonPropertyName("wardsKilled")] public int WardsKilled { get; set; }
            [JsonPropertyName("visionWardsBoughtInGame")] public int VisionWardsBoughtInGame { get; set; }
            [JsonPropertyName("sightWardsBoughtInGame")] public int SightWardsBoughtInGame { get; set; }

            // 伤害数据
            [JsonPropertyName("totalDamageDealt")] public int TotalDamageDealt { get; set; }

            [JsonPropertyName("totalDamageDealtToChampions")] public int TotalDamageDealtToChampions { get; set; }
            [JsonPropertyName("magicDamageDealt")] public int MagicDamageDealt { get; set; }
            [JsonPropertyName("magicDamageDealtToChampions")] public int MagicDamageDealtToChampions { get; set; }
            [JsonPropertyName("physicalDamageDealt")] public int PhysicalDamageDealt { get; set; }
            [JsonPropertyName("physicalDamageDealtToChampions")] public int PhysicalDamageDealtToChampions { get; set; }
            [JsonPropertyName("trueDamageDealt")] public int TrueDamageDealt { get; set; }
            [JsonPropertyName("trueDamageDealtToChampions")] public int TrueDamageDealtToChampions { get; set; }

            [JsonPropertyName("totalDamageTaken")] public int TotalDamageTaken { get; set; }
            [JsonPropertyName("magicalDamageTaken")] public int MagicalDamageTaken { get; set; }
            [JsonPropertyName("physicalDamageTaken")] public int PhysicalDamageTaken { get; set; }
            [JsonPropertyName("trueDamageTaken")] public int TrueDamageTaken { get; set; }

            [JsonPropertyName("damageSelfMitigated")] public int DamageSelfMitigated { get; set; }
            [JsonPropertyName("damageDealtToObjectives")] public int DamageDealtToObjectives { get; set; }
            [JsonPropertyName("damageDealtToTurrets")] public int DamageDealtToTurrets { get; set; }

            // 治疗和控制
            [JsonPropertyName("totalHeal")] public int TotalHeal { get; set; }

            [JsonPropertyName("totalUnitsHealed")] public int TotalUnitsHealed { get; set; }
            [JsonPropertyName("timeCCingOthers")] public int TimeCCingOthers { get; set; }
            [JsonPropertyName("totalTimeCrowdControlDealt")] public int TotalTimeCrowdControlDealt { get; set; }

            // 装备信息
            [JsonPropertyName("item0")] public int Item0 { get; set; }

            [JsonPropertyName("item1")] public int Item1 { get; set; }
            [JsonPropertyName("item2")] public int Item2 { get; set; }
            [JsonPropertyName("item3")] public int Item3 { get; set; }
            [JsonPropertyName("item4")] public int Item4 { get; set; }
            [JsonPropertyName("item5")] public int Item5 { get; set; }
            [JsonPropertyName("item6")] public int Item6 { get; set; }

            // 符文信息
            [JsonPropertyName("perk0")] public int Perk0 { get; set; }

            [JsonPropertyName("perk0Var1")] public int Perk0Var1 { get; set; }
            [JsonPropertyName("perk0Var2")] public int Perk0Var2 { get; set; }
            [JsonPropertyName("perk0Var3")] public int Perk0Var3 { get; set; }

            [JsonPropertyName("perkPrimaryStyle")] public int PerkPrimaryStyle { get; set; }
            [JsonPropertyName("perkSubStyle")] public int PerkSubStyle { get; set; }

            // 特殊模式数据
            [JsonPropertyName("playerAugment1")] public int PlayerAugment1 { get; set; }

            [JsonPropertyName("playerAugment2")] public int PlayerAugment2 { get; set; }
            [JsonPropertyName("playerAugment3")] public int PlayerAugment3 { get; set; }
            [JsonPropertyName("playerAugment4")] public int PlayerAugment4 { get; set; }
            [JsonPropertyName("playerAugment5")] public int PlayerAugment5 { get; set; }
            [JsonPropertyName("playerAugment6")] public int PlayerAugment6 { get; set; }

            // 胜负结果
            [JsonPropertyName("win")] public bool Win { get; set; }

            // 投降相关
            [JsonPropertyName("causedEarlySurrender")] public bool CausedEarlySurrender { get; set; }

            [JsonPropertyName("earlySurrenderAccomplice")] public bool EarlySurrenderAccomplice { get; set; }
            [JsonPropertyName("gameEndedInEarlySurrender")] public bool GameEndedInEarlySurrender { get; set; }
            [JsonPropertyName("gameEndedInSurrender")] public bool GameEndedInSurrender { get; set; }
            [JsonPropertyName("teamEarlySurrendered")] public bool TeamEarlySurrendered { get; set; }

            // 首杀/首塔等成就
            [JsonPropertyName("firstBloodKill")] public bool FirstBloodKill { get; set; }

            [JsonPropertyName("firstBloodAssist")] public bool FirstBloodAssist { get; set; }
            [JsonPropertyName("firstTowerKill")] public bool FirstTowerKill { get; set; }
            [JsonPropertyName("firstTowerAssist")] public bool FirstTowerAssist { get; set; }
            [JsonPropertyName("firstInhibitorKill")] public bool FirstInhibitorKill { get; set; }
            [JsonPropertyName("firstInhibitorAssist")] public bool FirstInhibitorAssist { get; set; }

            // 建筑击杀
            [JsonPropertyName("turretKills")] public int TurretKills { get; set; }

            [JsonPropertyName("inhibitorKills")] public int InhibitorKills { get; set; }

            // 其他统计
            [JsonPropertyName("largestCriticalStrike")] public int LargestCriticalStrike { get; set; }

            [JsonPropertyName("longestTimeSpentLiving")] public int LongestTimeSpentLiving { get; set; }
            [JsonPropertyName("playerSubteamId")] public int PlayerSubteamId { get; set; }
            [JsonPropertyName("subteamPlacement")] public int SubteamPlacement { get; set; }
        }

        /// <summary>
        /// 参与者时间线数据
        /// </summary>
        public class ParticipantTimeline
        {
            /// <summary>
            /// 每分钟补刀数变化
            /// </summary>
            [JsonPropertyName("creepsPerMinDeltas")]
            public Dictionary<string, double>? CreepsPerMinDeltas { get; set; }

            /// <summary>
            /// 分路位置（TOP/MIDDLE/JUNGLE/BOTTOM/SUPPORT）
            /// </summary>
            [JsonPropertyName("lane")]
            public string? Lane { get; set; }

            /// <summary>
            /// 参与者ID
            /// </summary>
            [JsonPropertyName("participantId")]
            public int ParticipantId { get; set; }

            /// <summary>
            /// 角色定位（SOLO/DUO等）
            /// </summary>
            [JsonPropertyName("role")]
            public string? Role { get; set; }

            // 其他时间线数据...
        }

        /// <summary>
        /// 队伍数据
        /// </summary>
        public class Team
        {
            /// <summary>
            /// Ban掉的英雄列表
            /// </summary>
            [JsonPropertyName("bans")]
            public List<BanInfo>? Bans { get; set; }

            /// <summary>
            /// 男爵击杀数
            /// </summary>
            [JsonPropertyName("baronKills")]
            public int BaronKills { get; set; }

            /// <summary>
            /// 统治战场胜利分数
            /// </summary>
            [JsonPropertyName("dominionVictoryScore")]
            public int DominionVictoryScore { get; set; }

            /// <summary>
            /// 小龙击杀数
            /// </summary>
            [JsonPropertyName("dragonKills")]
            public int DragonKills { get; set; }

            // 首杀/首塔等成就
            [JsonPropertyName("firstBaron")] public bool FirstBaron { get; set; }

            [JsonPropertyName("firstBlood")] public bool FirstBlood { get; set; }
            [JsonPropertyName("firstDargon")] public bool FirstDargon { get; set; }
            [JsonPropertyName("firstInhibitor")] public bool FirstInhibitor { get; set; }
            [JsonPropertyName("firstTower")] public bool FirstTower { get; set; }

            /// <summary>
            /// 队伍ID（100=蓝色方，200=红色方）
            /// </summary>
            [JsonPropertyName("teamId")]
            public int TeamId { get; set; }

            /// <summary>
            /// 防御塔击杀数
            /// </summary>
            [JsonPropertyName("towerKills")]
            public int TowerKills { get; set; }

            /// <summary>
            /// 水晶击杀数
            /// </summary>
            [JsonPropertyName("inhibitorKills")]
            public int InhibitorKills { get; set; }

            /// <summary>
            /// 胜负结果（Win/Fail）
            /// </summary>
            [JsonPropertyName("win")]
            public string? Win { get; set; }
        }

        /// <summary>
        /// Ban选信息
        /// </summary>
        public class BanInfo
        {
            /// <summary>
            /// 英雄ID（-1表示空Ban）
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
}