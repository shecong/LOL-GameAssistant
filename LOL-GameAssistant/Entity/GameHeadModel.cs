
namespace LOL_GameAssistant.Entity
{
    /// <summary>
    /// 比赛记录 JSON 模型（LCU /lol-match-history/v1/products/lol/{puuid}/matches）。
    /// </summary>
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
            [Newtonsoft.Json.JsonProperty("accountId")]
            public long AccountId { get; set; }

            /// <summary>
            /// 游戏数据容器
            /// </summary>
            [Newtonsoft.Json.JsonProperty("games")]
            public GamesContainer? Games { get; set; }

            /// <summary>
            /// 游戏平台ID（如：HN1=峡谷之巅）
            /// </summary>
            [Newtonsoft.Json.JsonProperty("platformId")]
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
            [Newtonsoft.Json.JsonProperty("gameBeginDate")]
            public string? GameBeginDate { get; set; }

            /// <summary>
            /// 总游戏场数
            /// </summary>
            [Newtonsoft.Json.JsonProperty("gameCount")]
            public int GameCount { get; set; }

            /// <summary>
            /// 游戏结束日期范围
            /// </summary>
            [Newtonsoft.Json.JsonProperty("gameEndDate")]
            public string? GameEndDate { get; set; }

            /// <summary>
            /// 当前返回的游戏起始索引
            /// </summary>
            [Newtonsoft.Json.JsonProperty("gameIndexBegin")]
            public int GameIndexBegin { get; set; }

            /// <summary>
            /// 当前返回的游戏结束索引
            /// </summary>
            [Newtonsoft.Json.JsonProperty("gameIndexEnd")]
            public int GameIndexEnd { get; set; }

            /// <summary>
            /// 具体的游戏数据数组
            /// </summary>
            [Newtonsoft.Json.JsonProperty("games")]
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
            [Newtonsoft.Json.JsonProperty("endOfGameResult")]
            public string? EndOfGameResult { get; set; }

            /// <summary>
            /// 游戏创建时间戳（毫秒）
            /// </summary>
            [Newtonsoft.Json.JsonProperty("gameCreation")]
            public long GameCreation { get; set; }

            /// <summary>
            /// ISO格式游戏创建时间
            /// </summary>
            [Newtonsoft.Json.JsonProperty("gameCreationDate")]
            public DateTime GameCreationDate { get; set; }

            /// <summary>
            /// 游戏时长（秒）
            /// </summary>
            [Newtonsoft.Json.JsonProperty("gameDuration")]
            public int GameDuration { get; set; }

            /// <summary>
            /// 游戏唯一ID
            /// </summary>
            [Newtonsoft.Json.JsonProperty("gameId")]
            public long GameId { get; set; }

            /// <summary>
            /// 游戏模式（CLASSIC=经典，RUBY_TRIAL_1=红宝石试炼，CHERRY=新模式）
            /// </summary>
            [Newtonsoft.Json.JsonProperty("gameMode")]
            public string? GameMode { get; set; }

            /// <summary>
            /// 游戏模式修饰器
            /// </summary>
            [Newtonsoft.Json.JsonProperty("gameModeMutators")]
            public List<string>? GameModeMutators { get; set; }

            /// <summary>
            /// 游戏类型（MATCHED_GAME=匹配游戏）
            /// </summary>
            [Newtonsoft.Json.JsonProperty("gameType")]
            public string? GameType { get; set; }

            /// <summary>
            /// 游戏版本号
            /// </summary>
            [Newtonsoft.Json.JsonProperty("gameVersion")]
            public string? GameVersion { get; set; }

            /// <summary>
            /// 地图ID（11=召唤师峡谷，30=其他地图）
            /// </summary>
            [Newtonsoft.Json.JsonProperty("mapId")]
            public int MapId { get; set; }

            /// <summary>
            /// 参与者身份信息
            /// </summary>
            [Newtonsoft.Json.JsonProperty("participantIdentities")]
            public List<ParticipantIdentity>? ParticipantIdentities { get; set; }

            /// <summary>
            /// 参与者游戏数据
            /// </summary>
            [Newtonsoft.Json.JsonProperty("participants")]
            public List<Participant>? Participants { get; set; }

            /// <summary>
            /// 游戏平台ID
            /// </summary>
            [Newtonsoft.Json.JsonProperty("platformId")]
            public string? PlatformId { get; set; }

            /// <summary>
            /// 队列ID（420=单排/双排，1700/4240=特殊模式）
            /// </summary>
            [Newtonsoft.Json.JsonProperty("queueId")]
            public int QueueId { get; set; }

            /// <summary>
            /// 赛季ID
            /// </summary>
            [Newtonsoft.Json.JsonProperty("seasonId")]
            public int SeasonId { get; set; }

            /// <summary>
            /// 队伍数据
            /// </summary>
            [Newtonsoft.Json.JsonProperty("teams")]
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
            [Newtonsoft.Json.JsonProperty("participantId")]
            public int ParticipantId { get; set; }

            /// <summary>
            /// 玩家信息
            /// </summary>
            [Newtonsoft.Json.JsonProperty("player")]
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
            [Newtonsoft.Json.JsonProperty("accountId")]
            public long AccountId { get; set; }

            /// <summary>
            /// 当前账号ID
            /// </summary>
            [Newtonsoft.Json.JsonProperty("currentAccountId")]
            public long CurrentAccountId { get; set; }

            /// <summary>
            /// 当前平台ID
            /// </summary>
            [Newtonsoft.Json.JsonProperty("currentPlatformId")]
            public string? CurrentPlatformId { get; set; }

            /// <summary>
            /// 游戏昵称
            /// </summary>
            [Newtonsoft.Json.JsonProperty("gameName")]
            public string? GameName { get; set; }

            /// <summary>
            /// 比赛历史URI
            /// </summary>
            [Newtonsoft.Json.JsonProperty("matchHistoryUri")]
            public string? MatchHistoryUri { get; set; }

            /// <summary>
            /// 平台ID
            /// </summary>
            [Newtonsoft.Json.JsonProperty("platformId")]
            public string? PlatformId { get; set; }

            /// <summary>
            /// 头像ID
            /// </summary>
            [Newtonsoft.Json.JsonProperty("profileIcon")]
            public int ProfileIcon { get; set; }

            /// <summary>
            /// 玩家唯一标识
            /// </summary>
            [Newtonsoft.Json.JsonProperty("puuid")]
            public string? Puuid { get; set; }

            /// <summary>
            /// 召唤师ID
            /// </summary>
            [Newtonsoft.Json.JsonProperty("summonerId")]
            public long SummonerId { get; set; }

            /// <summary>
            /// 召唤师名称（已废弃，使用GameName和TagLine）
            /// </summary>
            [Newtonsoft.Json.JsonProperty("summonerName")]
            public string? SummonerName { get; set; }

            /// <summary>
            /// 标签线（如#42483）
            /// </summary>
            [Newtonsoft.Json.JsonProperty("tagLine")]
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
            [Newtonsoft.Json.JsonProperty("championId")]
            public int ChampionId { get; set; }

            /// <summary>
            /// 达到的最高赛季段位
            /// </summary>
            [Newtonsoft.Json.JsonProperty("highestAchievedSeasonTier")]
            public string? HighestAchievedSeasonTier { get; set; }

            /// <summary>
            /// 参与者ID
            /// </summary>
            [Newtonsoft.Json.JsonProperty("participantId")]
            public int ParticipantId { get; set; }

            /// <summary>
            /// 召唤师技能1 ID
            /// </summary>
            [Newtonsoft.Json.JsonProperty("spell1Id")]
            public int Spell1Id { get; set; }

            /// <summary>
            /// 召唤师技能2 ID
            /// </summary>
            [Newtonsoft.Json.JsonProperty("spell2Id")]
            public int Spell2Id { get; set; }

            /// <summary>
            /// 游戏统计数据
            /// </summary>
            [Newtonsoft.Json.JsonProperty("stats")]
            public ParticipantStats? Stats { get; set; }

            /// <summary>
            /// 队伍ID（100=蓝色方，200=红色方）
            /// </summary>
            [Newtonsoft.Json.JsonProperty("teamId")]
            public int TeamId { get; set; }

            /// <summary>
            /// 时间线数据
            /// </summary>
            [Newtonsoft.Json.JsonProperty("timeline")]
            public ParticipantTimeline? Timeline { get; set; }
        }

        /// <summary>
        /// 参与者游戏统计数据
        /// </summary>
        public class ParticipantStats
        {
            // KDA相关
            [Newtonsoft.Json.JsonProperty("assists")] public int Assists { get; set; }

            [Newtonsoft.Json.JsonProperty("deaths")] public int Deaths { get; set; }
            [Newtonsoft.Json.JsonProperty("kills")] public int Kills { get; set; }

            // 连杀数据
            [Newtonsoft.Json.JsonProperty("doubleKills")] public int DoubleKills { get; set; }

            [Newtonsoft.Json.JsonProperty("tripleKills")] public int TripleKills { get; set; }
            [Newtonsoft.Json.JsonProperty("quadraKills")] public int QuadraKills { get; set; }
            [Newtonsoft.Json.JsonProperty("pentaKills")] public int PentaKills { get; set; }
            [Newtonsoft.Json.JsonProperty("killingSprees")] public int KillingSprees { get; set; }
            [Newtonsoft.Json.JsonProperty("largestKillingSpree")] public int LargestKillingSpree { get; set; }
            [Newtonsoft.Json.JsonProperty("largestMultiKill")] public int LargestMultiKill { get; set; }

            // 基础数据
            [Newtonsoft.Json.JsonProperty("champLevel")] public int ChampLevel { get; set; }

            [Newtonsoft.Json.JsonProperty("goldEarned")] public int GoldEarned { get; set; }
            [Newtonsoft.Json.JsonProperty("goldSpent")] public int GoldSpent { get; set; }

            // 补刀数据
            [Newtonsoft.Json.JsonProperty("totalMinionsKilled")] public int TotalMinionsKilled { get; set; }

            [Newtonsoft.Json.JsonProperty("neutralMinionsKilled")] public int NeutralMinionsKilled { get; set; }
            [Newtonsoft.Json.JsonProperty("neutralMinionsKilledEnemyJungle")] public int NeutralMinionsKilledEnemyJungle { get; set; }
            [Newtonsoft.Json.JsonProperty("neutralMinionsKilledTeamJungle")] public int NeutralMinionsKilledTeamJungle { get; set; }

            // 视野数据
            [Newtonsoft.Json.JsonProperty("visionScore")] public int VisionScore { get; set; }

            [Newtonsoft.Json.JsonProperty("wardsPlaced")] public int WardsPlaced { get; set; }
            [Newtonsoft.Json.JsonProperty("wardsKilled")] public int WardsKilled { get; set; }
            [Newtonsoft.Json.JsonProperty("visionWardsBoughtInGame")] public int VisionWardsBoughtInGame { get; set; }
            [Newtonsoft.Json.JsonProperty("sightWardsBoughtInGame")] public int SightWardsBoughtInGame { get; set; }

            // 伤害数据
            [Newtonsoft.Json.JsonProperty("totalDamageDealt")] public int TotalDamageDealt { get; set; }

            [Newtonsoft.Json.JsonProperty("totalDamageDealtToChampions")] public int TotalDamageDealtToChampions { get; set; }
            [Newtonsoft.Json.JsonProperty("magicDamageDealt")] public int MagicDamageDealt { get; set; }
            [Newtonsoft.Json.JsonProperty("magicDamageDealtToChampions")] public int MagicDamageDealtToChampions { get; set; }
            [Newtonsoft.Json.JsonProperty("physicalDamageDealt")] public int PhysicalDamageDealt { get; set; }
            [Newtonsoft.Json.JsonProperty("physicalDamageDealtToChampions")] public int PhysicalDamageDealtToChampions { get; set; }
            [Newtonsoft.Json.JsonProperty("trueDamageDealt")] public int TrueDamageDealt { get; set; }
            [Newtonsoft.Json.JsonProperty("trueDamageDealtToChampions")] public int TrueDamageDealtToChampions { get; set; }

            [Newtonsoft.Json.JsonProperty("totalDamageTaken")] public int TotalDamageTaken { get; set; }
            [Newtonsoft.Json.JsonProperty("magicalDamageTaken")] public int MagicalDamageTaken { get; set; }
            [Newtonsoft.Json.JsonProperty("physicalDamageTaken")] public int PhysicalDamageTaken { get; set; }
            [Newtonsoft.Json.JsonProperty("trueDamageTaken")] public int TrueDamageTaken { get; set; }

            [Newtonsoft.Json.JsonProperty("damageSelfMitigated")] public int DamageSelfMitigated { get; set; }
            [Newtonsoft.Json.JsonProperty("damageDealtToObjectives")] public int DamageDealtToObjectives { get; set; }
            [Newtonsoft.Json.JsonProperty("damageDealtToTurrets")] public int DamageDealtToTurrets { get; set; }

            // 治疗和控制
            [Newtonsoft.Json.JsonProperty("totalHeal")] public int TotalHeal { get; set; }

            [Newtonsoft.Json.JsonProperty("totalUnitsHealed")] public int TotalUnitsHealed { get; set; }
            [Newtonsoft.Json.JsonProperty("timeCCingOthers")] public int TimeCCingOthers { get; set; }
            [Newtonsoft.Json.JsonProperty("totalTimeCrowdControlDealt")] public int TotalTimeCrowdControlDealt { get; set; }

            // 装备信息
            [Newtonsoft.Json.JsonProperty("item0")] public int Item0 { get; set; }

            [Newtonsoft.Json.JsonProperty("item1")] public int Item1 { get; set; }
            [Newtonsoft.Json.JsonProperty("item2")] public int Item2 { get; set; }
            [Newtonsoft.Json.JsonProperty("item3")] public int Item3 { get; set; }
            [Newtonsoft.Json.JsonProperty("item4")] public int Item4 { get; set; }
            [Newtonsoft.Json.JsonProperty("item5")] public int Item5 { get; set; }
            [Newtonsoft.Json.JsonProperty("item6")] public int Item6 { get; set; }

            // 符文信息
            [Newtonsoft.Json.JsonProperty("perk0")] public int Perk0 { get; set; }

            [Newtonsoft.Json.JsonProperty("perk0Var1")] public int Perk0Var1 { get; set; }
            [Newtonsoft.Json.JsonProperty("perk0Var2")] public int Perk0Var2 { get; set; }
            [Newtonsoft.Json.JsonProperty("perk0Var3")] public int Perk0Var3 { get; set; }

            [Newtonsoft.Json.JsonProperty("perkPrimaryStyle")] public int PerkPrimaryStyle { get; set; }
            [Newtonsoft.Json.JsonProperty("perkSubStyle")] public int PerkSubStyle { get; set; }

            // 特殊模式数据
            [Newtonsoft.Json.JsonProperty("playerAugment1")] public int PlayerAugment1 { get; set; }

            [Newtonsoft.Json.JsonProperty("playerAugment2")] public int PlayerAugment2 { get; set; }
            [Newtonsoft.Json.JsonProperty("playerAugment3")] public int PlayerAugment3 { get; set; }
            [Newtonsoft.Json.JsonProperty("playerAugment4")] public int PlayerAugment4 { get; set; }
            [Newtonsoft.Json.JsonProperty("playerAugment5")] public int PlayerAugment5 { get; set; }
            [Newtonsoft.Json.JsonProperty("playerAugment6")] public int PlayerAugment6 { get; set; }

            // 胜负结果
            [Newtonsoft.Json.JsonProperty("win")] public bool Win { get; set; }

            // 投降相关
            [Newtonsoft.Json.JsonProperty("causedEarlySurrender")] public bool CausedEarlySurrender { get; set; }

            [Newtonsoft.Json.JsonProperty("earlySurrenderAccomplice")] public bool EarlySurrenderAccomplice { get; set; }
            [Newtonsoft.Json.JsonProperty("gameEndedInEarlySurrender")] public bool GameEndedInEarlySurrender { get; set; }
            [Newtonsoft.Json.JsonProperty("gameEndedInSurrender")] public bool GameEndedInSurrender { get; set; }
            [Newtonsoft.Json.JsonProperty("teamEarlySurrendered")] public bool TeamEarlySurrendered { get; set; }

            // 首杀/首塔等成就
            [Newtonsoft.Json.JsonProperty("firstBloodKill")] public bool FirstBloodKill { get; set; }

            [Newtonsoft.Json.JsonProperty("firstBloodAssist")] public bool FirstBloodAssist { get; set; }
            [Newtonsoft.Json.JsonProperty("firstTowerKill")] public bool FirstTowerKill { get; set; }
            [Newtonsoft.Json.JsonProperty("firstTowerAssist")] public bool FirstTowerAssist { get; set; }
            [Newtonsoft.Json.JsonProperty("firstInhibitorKill")] public bool FirstInhibitorKill { get; set; }
            [Newtonsoft.Json.JsonProperty("firstInhibitorAssist")] public bool FirstInhibitorAssist { get; set; }

            // 建筑击杀
            [Newtonsoft.Json.JsonProperty("turretKills")] public int TurretKills { get; set; }

            [Newtonsoft.Json.JsonProperty("inhibitorKills")] public int InhibitorKills { get; set; }

            // 其他统计
            [Newtonsoft.Json.JsonProperty("largestCriticalStrike")] public int LargestCriticalStrike { get; set; }

            [Newtonsoft.Json.JsonProperty("longestTimeSpentLiving")] public int LongestTimeSpentLiving { get; set; }
            [Newtonsoft.Json.JsonProperty("playerSubteamId")] public int PlayerSubteamId { get; set; }
            [Newtonsoft.Json.JsonProperty("subteamPlacement")] public int SubteamPlacement { get; set; }
        }

        /// <summary>
        /// 参与者时间线数据
        /// </summary>
        public class ParticipantTimeline
        {
            /// <summary>
            /// 每分钟补刀数变化
            /// </summary>
            [Newtonsoft.Json.JsonProperty("creepsPerMinDeltas")]
            public Dictionary<string, double>? CreepsPerMinDeltas { get; set; }

            /// <summary>
            /// 分路位置（TOP/MIDDLE/JUNGLE/BOTTOM/SUPPORT）
            /// </summary>
            [Newtonsoft.Json.JsonProperty("lane")]
            public string? Lane { get; set; }

            /// <summary>
            /// 参与者ID
            /// </summary>
            [Newtonsoft.Json.JsonProperty("participantId")]
            public int ParticipantId { get; set; }

            /// <summary>
            /// 角色定位（SOLO/DUO等）
            /// </summary>
            [Newtonsoft.Json.JsonProperty("role")]
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
            [Newtonsoft.Json.JsonProperty("bans")]
            public List<BanInfo>? Bans { get; set; }

            /// <summary>
            /// 男爵击杀数
            /// </summary>
            [Newtonsoft.Json.JsonProperty("baronKills")]
            public int BaronKills { get; set; }

            /// <summary>
            /// 统治战场胜利分数
            /// </summary>
            [Newtonsoft.Json.JsonProperty("dominionVictoryScore")]
            public int DominionVictoryScore { get; set; }

            /// <summary>
            /// 小龙击杀数
            /// </summary>
            [Newtonsoft.Json.JsonProperty("dragonKills")]
            public int DragonKills { get; set; }

            // 首杀/首塔等成就
            [Newtonsoft.Json.JsonProperty("firstBaron")] public bool FirstBaron { get; set; }

            [Newtonsoft.Json.JsonProperty("firstBlood")] public bool FirstBlood { get; set; }
            [Newtonsoft.Json.JsonProperty("firstDargon")] public bool FirstDargon { get; set; }
            [Newtonsoft.Json.JsonProperty("firstInhibitor")] public bool FirstInhibitor { get; set; }
            [Newtonsoft.Json.JsonProperty("firstTower")] public bool FirstTower { get; set; }

            /// <summary>
            /// 队伍ID（100=蓝色方，200=红色方）
            /// </summary>
            [Newtonsoft.Json.JsonProperty("teamId")]
            public int TeamId { get; set; }

            /// <summary>
            /// 防御塔击杀数
            /// </summary>
            [Newtonsoft.Json.JsonProperty("towerKills")]
            public int TowerKills { get; set; }

            /// <summary>
            /// 水晶击杀数
            /// </summary>
            [Newtonsoft.Json.JsonProperty("inhibitorKills")]
            public int InhibitorKills { get; set; }

            /// <summary>
            /// 胜负结果（Win/Fail）
            /// </summary>
            [Newtonsoft.Json.JsonProperty("win")]
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
            [Newtonsoft.Json.JsonProperty("championId")]
            public int ChampionId { get; set; }

            /// <summary>
            /// Ban选顺序
            /// </summary>
            [Newtonsoft.Json.JsonProperty("pickTurn")]
            public int PickTurn { get; set; }
        }
    }
}
