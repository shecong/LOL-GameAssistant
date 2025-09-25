using LOL_GameAssistant.Models;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace LOL_GameAssistant.Models
{
    public class GameDetailModel
    {
        // <summary>
        // 代表一个玩家（召唤师）的基本信息。
        // </summary>
        public class Player
        {
            /// <summary>
            /// 账号ID (旧版系统，通常为0)。
            /// </summary>
            public int accountId { get; set; }

            /// <summary>
            /// 当前账号ID (旧版系统，通常为0)。
            /// </summary>
            public int currentAccountId { get; set; }

            /// <summary>
            /// 当前平台ID，例如 "HN1" 代表诺克萨斯服务器。
            /// </summary>
            public string currentPlatformId { get; set; }

            /// <summary>
            /// 玩家的游戏名称（不含标签）。
            /// </summary>
            public string gameName { get; set; }

            /// <summary>
            /// 对战历史记录的URI路径（在此数据中通常为空）。
            /// </summary>
            public string matchHistoryUri { get; set; }

            /// <summary>
            /// 平台ID，例如 "HN1"。
            /// </summary>
            public string platformId { get; set; }

            /// <summary>
            /// 玩家的头像图标ID。
            /// </summary>
            public int profileIcon { get; set; }

            /// <summary>
            /// 玩家的PUUID (Universally Unique Identifier)，一个全球唯一的玩家标识符。
            /// </summary>
            public string puuid { get; set; }

            /// <summary>
            /// 召唤师ID。
            /// </summary>
            public long summonerId { get; set; }

            /// <summary>
            /// 召唤师名称（在此数据中通常为空，名称在 gameName 中）。
            /// </summary>
            public string summonerName { get; set; }

            /// <summary>
            /// 玩家名后的标签线，例如 "83580"。
            /// </summary>
            public string tagLine { get; set; }
        }

        // <summary>
        // 参与者身份信息，将 participantId 与 Player 对象关联起来。
        // </summary>
        public class ParticipantIdentitiesItem
        {
            /// <summary>
            /// 参与者ID，用于在 participants 数组中索引该玩家的详细数据。
            /// </summary>
            public int participantId { get; set; }

            /// <summary>
            /// 该参与者对应的玩家详细信息。
            /// </summary>
            public Player player { get; set; }
        }

        // <summary>
        // 玩家在一局游戏中的详细统计数据。
        // </summary>
        public class Stats
        {
            /// <summary>
            /// 助攻数。
            /// </summary>
            public int assists { get; set; }

            /// <summary>
            /// 是否导致了提前投降。在“终极魔典”模式中通常为 false。
            /// </summary>
            [JsonConverter(typeof(JsonStringBoolConverter))]
            public bool causedEarlySurrender { get; set; }

            /// <summary>
            /// 游戏结束时的英雄等级。
            /// </summary>
            public int champLevel { get; set; }

            /// <summary>
            /// 战斗玩家得分。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int combatPlayerScore { get; set; }

            /// <summary>
            /// 对目标（如防御塔、史诗野怪）造成的伤害。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int damageDealtToObjectives { get; set; }

            /// <summary>
            /// 对防御塔造成的伤害。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int damageDealtToTurrets { get; set; }

            /// <summary>
            /// 自我减免的伤害量（通过护甲、魔抗、技能减伤等）。
            /// </summary>
            public int damageSelfMitigated { get; set; }

            /// <summary>
            /// 死亡次数。
            /// </summary>
            public int deaths { get; set; }

            /// <summary>
            /// 双杀次数。
            /// </summary>
            public int doubleKills { get; set; }

            /// <summary>
            /// 是否是提前投降的帮凶。在“终极魔典”模式中通常为 false。
            /// </summary>
            [JsonConverter(typeof(JsonStringBoolConverter))]
            public bool earlySurrenderAccomplice { get; set; }

            /// <summary>
            /// 是否参与第一滴血（助攻）。
            /// </summary>
            [JsonConverter(typeof(JsonStringBoolConverter))]
            public bool firstBloodAssist { get; set; }

            /// <summary>
            /// 是否获得第一滴血。
            /// </summary>
            [JsonConverter(typeof(JsonStringBoolConverter))]
            public bool firstBloodKill { get; set; }

            /// <summary>
            /// 是否参与摧毁第一个水晶（助攻）。在“终极魔典”模式中通常为 false。
            /// </summary>
            [JsonConverter(typeof(JsonStringBoolConverter))]
            public bool firstInhibitorAssist { get; set; }

            /// <summary>
            /// 是否摧毁第一个水晶。在“终极魔典”模式中通常为 false。
            /// </summary>
            [JsonConverter(typeof(JsonStringBoolConverter))]
            public bool firstInhibitorKill { get; set; }

            /// <summary>
            /// 是否参与摧毁第一座防御塔（助攻）。在“终极魔典”模式中通常为 false。
            /// </summary>
            [JsonConverter(typeof(JsonStringBoolConverter))]
            public bool firstTowerAssist { get; set; }

            /// <summary>
            /// 是否摧毁第一座防御塔。在“终极魔典”模式中通常为 false。
            /// </summary>
            [JsonConverter(typeof(JsonStringBoolConverter))]
            public bool firstTowerKill { get; set; }

            /// <summary>
            /// 游戏是否因提前投降而结束。
            /// </summary>
            [JsonConverter(typeof(JsonStringBoolConverter))]
            public bool gameEndedInEarlySurrender { get; set; }

            /// <summary>
            /// 游戏是否因投降而结束。
            /// </summary>
            [JsonConverter(typeof(JsonStringBoolConverter))]
            public bool gameEndedInSurrender { get; set; }

            /// <summary>
            /// 赚取的总金币。
            /// </summary>
            public int goldEarned { get; set; }

            /// <summary>
            /// 花费的总金币。
            /// </summary>
            public int goldSpent { get; set; }

            /// <summary>
            /// 摧毁的水晶数量。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int inhibitorKills { get; set; }

            /// <summary>
            /// 物品栏第1个物品的ID。
            /// </summary>
            public int item0 { get; set; }

            /// <summary>
            /// 物品栏第2个物品的ID。
            /// </summary>
            public int item1 { get; set; }

            /// <summary>
            /// 物品栏第3个物品的ID。
            /// </summary>
            public int item2 { get; set; }

            /// <summary>
            /// 物品栏第4个物品的ID。
            /// </summary>
            public int item3 { get; set; }

            /// <summary>
            /// 物品栏第5个物品的ID。
            /// </summary>
            public int item4 { get; set; }

            /// <summary>
            /// 物品栏第6个物品的ID。
            /// </summary>
            public int item5 { get; set; }

            /// <summary>
            /// 饰品槽的物品ID。
            /// </summary>
            public int item6 { get; set; }

            /// <summary>
            /// 连杀次数。
            /// </summary>
            public int killingSprees { get; set; }

            /// <summary>
            /// 击杀数。
            /// </summary>
            public int kills { get; set; }

            /// <summary>
            /// 最大的暴击伤害值。
            /// </summary>
            public int largestCriticalStrike { get; set; }

            /// <summary>
            /// 最长的连杀人次。
            /// </summary>
            public int largestKillingSpree { get; set; }

            /// <summary>
            /// 最大的多杀次数（如双杀、三杀等）。
            /// </summary>
            public int largestMultiKill { get; set; }

            /// <summary>
            /// 最长存活时间（秒）。
            /// </summary>
            public int longestTimeSpentLiving { get; set; }

            /// <summary>
            /// 造成的魔法伤害总量。
            /// </summary>
            public int magicDamageDealt { get; set; }

            /// <summary>
            /// 对英雄造成的魔法伤害。
            /// </summary>
            public int magicDamageDealtToChampions { get; set; }

            /// <summary>
            /// 受到的魔法伤害。
            /// </summary>
            public int magicalDamageTaken { get; set; }

            /// <summary>
            /// 击杀的野怪总数。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int neutralMinionsKilled { get; set; }

            /// <summary>
            /// 在敌方野区击杀的野怪数。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int neutralMinionsKilledEnemyJungle { get; set; }

            /// <summary>
            /// 在我方野区击杀的野怪数。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int neutralMinionsKilledTeamJungle { get; set; }

            /// <summary>
            /// 目标玩家得分。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int objectivePlayerScore { get; set; }

            /// <summary>
            /// 参与者ID。
            /// </summary>
            public int participantId { get; set; }

            /// <summary>
            /// 五杀次数。
            /// </summary>
            public int pentaKills { get; set; }

            /// <summary>
            /// 符文0的ID。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int perk0 { get; set; }

            /// <summary>
            /// 符文0的变量1。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int perk0Var1 { get; set; }

            /// <summary>
            /// 符文0的变量2。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int perk0Var2 { get; set; }

            /// <summary>
            /// 符文0的变量3。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int perk0Var3 { get; set; }

            /// <summary>
            /// 符文1的ID。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int perk1 { get; set; }

            /// <summary>
            /// 符文1的变量1。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int perk1Var1 { get; set; }

            /// <summary>
            /// 符文1的变量2。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int perk1Var2 { get; set; }

            /// <summary>
            /// 符文1的变量3。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int perk1Var3 { get; set; }

            /// <summary>
            /// 符文2的ID。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int perk2 { get; set; }

            /// <summary>
            /// 符文2的变量1。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int perk2Var1 { get; set; }

            /// <summary>
            /// 符文2的变量2。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int perk2Var2 { get; set; }

            /// <summary>
            /// 符文2的变量3。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int perk2Var3 { get; set; }

            /// <summary>
            /// 符文3的ID。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int perk3 { get; set; }

            /// <summary>
            /// 符文3的变量1。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int perk3Var1 { get; set; }

            /// <summary>
            /// 符文3的变量2。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int perk3Var2 { get; set; }

            /// <summary>
            /// 符文3的变量3。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int perk3Var3 { get; set; }

            /// <summary>
            /// 符文4的ID。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int perk4 { get; set; }

            /// <summary>
            /// 符文4的变量1。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int perk4Var1 { get; set; }

            /// <summary>
            /// 符文4的变量2。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int perk4Var2 { get; set; }

            /// <summary>
            /// 符文4的变量3。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int perk4Var3 { get; set; }

            /// <summary>
            /// 符文5的ID。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int perk5 { get; set; }

            /// <summary>
            /// 符文5的变量1。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int perk5Var1 { get; set; }

            /// <summary>
            /// 符文5的变量2。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int perk5Var2 { get; set; }

            /// <summary>
            /// 符文5的变量3。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int perk5Var3 { get; set; }

            /// <summary>
            /// 主系符文风格ID。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int perkPrimaryStyle { get; set; }

            /// <summary>
            /// 副系符文风格ID。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int perkSubStyle { get; set; }

            /// <summary>
            /// 造成的物理伤害总量。
            /// </summary>
            public int physicalDamageDealt { get; set; }

            /// <summary>
            /// 对英雄造成的物理伤害。
            /// </summary>
            public int physicalDamageDealtToChampions { get; set; }

            /// <summary>
            /// 受到的物理伤害。
            /// </summary>
            public int physicalDamageTaken { get; set; }

            /// <summary>
            /// 玩家选择的第一个强化（终极魔典模式核心机制）。
            /// </summary>
            public int playerAugment1 { get; set; }

            /// <summary>
            /// 玩家选择的第二个强化。
            /// </summary>
            public int playerAugment2 { get; set; }

            /// <summary>
            /// 玩家选择的第三个强化。
            /// </summary>
            public int playerAugment3 { get; set; }

            /// <summary>
            /// 玩家选择的第四个强化（如果达到足够等级）。
            /// </summary>
            public int playerAugment4 { get; set; }

            /// <summary>
            /// 玩家选择的第五个强化（如果达到足够等级，通常为0）。
            /// </summary>
            public int playerAugment5 { get; set; }

            /// <summary>
            /// 玩家选择的第六个强化（如果达到足够等级，通常为0）。
            /// </summary>
            public int playerAugment6 { get; set; }

            /// <summary>
            /// 玩家自定义得分0。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int playerScore0 { get; set; }

            /// <summary>
            /// 玩家自定义得分1。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int playerScore1 { get; set; }

            /// <summary>
            /// 玩家自定义得分2。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int playerScore2 { get; set; }

            /// <summary>
            /// 玩家自定义得分3。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int playerScore3 { get; set; }

            /// <summary>
            /// 玩家自定义得分4。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int playerScore4 { get; set; }

            /// <summary>
            /// 玩家自定义得分5。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int playerScore5 { get; set; }

            /// <summary>
            /// 玩家自定义得分6。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int playerScore6 { get; set; }

            /// <summary>
            /// 玩家自定义得分7。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int playerScore7 { get; set; }

            /// <summary>
            /// 玩家自定义得分8。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int playerScore8 { get; set; }

            /// <summary>
            /// 玩家自定义得分9。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int playerScore9 { get; set; }

            /// <summary>
            /// 玩家所在的子团队ID（用于大乱斗、终极魔典等模式区分队伍）。
            /// </summary>
            public int playerSubteamId { get; set; }

            /// <summary>
            /// 四杀次数。
            /// </summary>
            public int quadraKills { get; set; }

            /// <summary>
            /// 购买的守卫数量。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int sightWardsBoughtInGame { get; set; }

            /// <summary>
            /// 子团队排名（1为第一名，2为第二名，以此类推）。
            /// </summary>
            public int subteamPlacement { get; set; }

            /// <summary>
            /// 玩家所在队伍是否提前投降。
            /// </summary>
            [JsonConverter(typeof(JsonStringBoolConverter))]
            public bool teamEarlySurrendered { get; set; }

            /// <summary>
            /// 控制其他英雄的总时间（秒）。
            /// </summary>
            public int timeCCingOthers { get; set; }

            /// <summary>
            /// 造成的总伤害量。
            /// </summary>
            public int totalDamageDealt { get; set; }

            /// <summary>
            /// 对英雄造成的总伤害量。
            /// </summary>
            public int totalDamageDealtToChampions { get; set; }

            /// <summary>
            /// 受到的总伤害量。
            /// </summary>
            public int totalDamageTaken { get; set; }

            /// <summary>
            /// 总治疗量（包括自我治疗和受到的治疗）。
            /// </summary>
            public int totalHeal { get; set; }

            /// <summary>
            /// 击杀的小兵总数。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int totalMinionsKilled { get; set; }

            /// <summary>
            /// 玩家总得分。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int totalPlayerScore { get; set; }

            /// <summary>
            /// 玩家得分排名。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int totalScoreRank { get; set; }

            /// <summary>
            /// 施加的总控制时间（秒）。
            /// </summary>
            public int totalTimeCrowdControlDealt { get; set; }

            /// <summary>
            /// 治疗的单位总数。
            /// </summary>
            public int totalUnitsHealed { get; set; }

            /// <summary>
            /// 三杀次数。
            /// </summary>
            public int tripleKills { get; set; }

            /// <summary>
            /// 造成的真实伤害总量。
            /// </summary>
            public int trueDamageDealt { get; set; }

            /// <summary>
            /// 对英雄造成的真实伤害。
            /// </summary>
            public int trueDamageDealtToChampions { get; set; }

            /// <summary>
            /// 受到的真实伤害。
            /// </summary>
            public int trueDamageTaken { get; set; }

            /// <summary>
            /// 摧毁的防御塔数量。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int turretKills { get; set; }

            /// <summary>
            /// 神话杀次数（非常罕见）。
            /// </summary>
            public int unrealKills { get; set; }

            /// <summary>
            /// 视野得分。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int visionScore { get; set; }

            /// <summary>
            /// 购买的真视守卫数量。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int visionWardsBoughtInGame { get; set; }

            /// <summary>
            /// 摧毁的守卫数量。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int wardsKilled { get; set; }

            /// <summary>
            /// 放置的守卫数量。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int wardsPlaced { get; set; }

            /// <summary>
            /// 是否获胜。
            /// </summary> 
            public String win { get; set; }
        }

        // <summary>
        // 玩家对局时间线数据，包含分路、角色和每分钟数据增量（在此数据中为空）。
        // </summary>
        public class Timeline
        {
            /// <summary>
            /// 每分钟补刀数增量。在“终极魔典”模式数据中通常为空对象。
            /// </summary>
            public object creepsPerMinDeltas { get; set; }

            /// <summary>
            /// 每分钟补刀差增量。在“终极魔典”模式数据中通常为空对象。
            /// </summary>
            public object csDiffPerMinDeltas { get; set; }

            /// <summary>
            /// 每分钟承受伤害差增量。在“终极魔典”模式数据中通常为空对象。
            /// </summary>
            public object damageTakenDiffPerMinDeltas { get; set; }

            /// <summary>
            /// 每分钟承受伤害增量。在“终极魔典”模式数据中通常为空对象。
            /// </summary>
            public object damageTakenPerMinDeltas { get; set; }

            /// <summary>
            /// 每分钟金币增量。在“终极魔典”模式数据中通常为空对象。
            /// </summary>
            public object goldPerMinDeltas { get; set; }

            /// <summary>
            /// 玩家的分路，例如 "TOP", "BOTTOM"。在“终极魔典”模式中，这可能是系统分配的，不一定代表实际分路。
            /// </summary>
            public string lane { get; set; }

            /// <summary>
            /// 玩者的角色，例如 "SUPPORT"。在“终极魔典”模式中，这可能是系统分配的，不一定代表实际角色。
            /// </summary>
            public string role { get; set; }

            /// <summary>
            /// 每分钟经验差增量。在“终极魔典”模式数据中通常为空对象。
            /// </summary>
            public object xpDiffPerMinDeltas { get; set; }

            /// <summary>
            /// 每分钟经验增量。在“终极魔典”模式数据中通常为空对象。
            /// </summary>
            public object xpPerMinDeltas { get; set; }
        }

        // <summary>
        // 参与者信息，包含玩家选择的英雄、召唤师技能、统计数据和时间线。
        // </summary>
        public class ParticipantsItem
        {
            /// <summary>
            /// 玩家选择的英雄ID。
            /// </summary>
            public int championId { get; set; }

            /// <summary>
            /// 玩家在过往赛季中达到的最高段位（在此数据中通常为空字符串）。
            /// </summary>
            public string highestAchievedSeasonTier { get; set; }

            /// <summary>
            /// 参与者ID。
            /// </summary>
            public int participantId { get; set; }

            /// <summary>
            /// 召唤师技能1的ID（D键技能）。
            /// </summary>
            public String spell1Id
            {
                get
                {
                    return spell1Id switch
                    {
                        "21" => "SummonerBarrier",
                        "1" => "SummonerBoost",
                        "2202" => "SummonerCherryFlash",
                        "2201" => "SummonerCherryHold",
                        "14" => "SummonerDot",
                        "3" => "SummonerExhaust",
                        "4" => "SummonerFlash",
                        "6" => "SummonerHaste",
                        "7" => "SummonerHeal",
                        "13" => "SummonerMana",
                        "30" => "SummonerPoroRecall",
                        "31" => "SummonerPoroThrow",
                        "11" => "SummonerSmite",
                        "39" => "SummonerSnowURFSnowball_Mark",
                        "32" => "SummonerSnowball",
                        "12" => "SummonerTeleport",
                        "54" => "Summoner_UltBookPlaceholder",
                        "55" => "Summoner_UltBookSmitePlaceholder",
                        _ => spell1Id // 默认返回原值
                    };
                }
                set => spell1Id = value;
            }

            /// <summary>
            /// 召唤师技能2的ID（F键技能）。
            /// </summary>
            public int spell2Id { get; set; }

            /// <summary>
            /// 该参与者的详细游戏统计数据。
            /// </summary>
            public Stats stats { get; set; }

            /// <summary>
            /// 玩家所在的队伍ID (100 或 200)。
            /// </summary>
            public int teamId { get; set; }

            /// <summary>
            /// 该参与者的游戏时间线数据。
            /// </summary>
            public Timeline timeline { get; set; }
        }

        // <summary>
        // 禁用英雄的信息。
        // </summary>
        public class BansItem
        {
            /// <summary>
            /// 被禁用英雄的ID。
            /// </summary>
            public int championId { get; set; }

            /// <summary>
            /// 禁用的顺序（第几手禁用）。
            /// </summary>
            public int pickTurn { get; set; }
        }

        // <summary>
        // 队伍信息，包含队伍的整体统计数据。
        // </summary>
        public class TeamsItem
        {
            /// <summary>
            /// 本局游戏中该队伍禁用的英雄列表。
            /// </summary>
            public List<BansItem> bans { get; set; }

            /// <summary>
            /// 击杀的男爵数量。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int baronKills { get; set; }

            /// <summary>
            /// 统治战场得分。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int dominionVictoryScore { get; set; }

            /// <summary>
            /// 击杀的元素亚龙数量。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int dragonKills { get; set; }

            /// <summary>
            /// 是否率先击杀男爵。在“终极魔典”模式中通常为 false。
            /// </summary>
            [JsonConverter(typeof(JsonStringBoolConverter))]
            public bool firstBaron { get; set; }

            /// <summary>
            /// 是否率先获得第一滴血。
            /// </summary>
            [JsonConverter(typeof(JsonStringBoolConverter))]
            public bool firstBlood { get; set; }

            /// <summary>
            /// 是否率先击杀第一条龙（属性名有拼写错误，应为 firstDragon）。在“终极魔典”模式中通常为 false。
            /// </summary>
            [JsonConverter(typeof(JsonStringBoolConverter))]
            public bool firstDargon { get; set; }

            /// <summary>
            /// 是否率先摧毁水晶。在“终极魔典”模式中通常为 false。
            /// </summary>
            [JsonConverter(typeof(JsonStringBoolConverter))]
            public bool firstInhibitor { get; set; }

            /// <summary>
            /// 是否率先摧毁防御塔。在“终极魔典”模式中通常为 false。
            /// </summary>
            [JsonConverter(typeof(JsonStringBoolConverter))]
            public bool firstTower { get; set; }

            /// <summary>
            /// 击杀的虚空兽群数量。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int hordeKills { get; set; }

            /// <summary>
            /// 摧毁的水晶数量。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int inhibitorKills { get; set; }

            /// <summary>
            /// 击杀的峡谷先锋数量。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int riftHeraldKills { get; set; }

            /// <summary>
            /// 队伍ID (100 或 200)。
            /// </summary>
            public int teamId { get; set; }

            /// <summary>
            /// 摧毁的防御塔数量。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int towerKills { get; set; }

            /// <summary>
            /// 击杀的大龙（扭曲树精或虚空巢虫）数量。在“终极魔典”模式中通常为 0。
            /// </summary>
            public int vilemawKills { get; set; }

            /// <summary>
            /// 队伍是否获胜。
            /// </summary> 
            public String win { get; set; }
        }

        // <summary>
        // 根对象，代表一局完整的游戏数据。
        // </summary>
        public class GameInfo
        {
            /// <summary>
            /// 游戏结束结果，例如 "GameComplete" 表示游戏正常结束。
            /// </summary>
            public string endOfGameResult { get; set; }

            /// <summary>
            /// 游戏创建时间的时间戳（毫秒）。
            /// </summary>
            public long gameCreation { get; set; }

            /// <summary>
            /// 游戏创建的日期时间字符串（UTC格式）。
            /// </summary>
            public string gameCreationDate { get; set; }

            /// <summary>
            /// 游戏持续时间（秒）。
            /// </summary>
            public int gameDuration { get; set; }

            /// <summary>
            /// 游戏的唯一ID。
            /// </summary>
            public long gameId { get; set; }

            /// <summary>
            /// 游戏模式，例如 "CHERRY" 代表“终极魔典”模式。
            /// </summary>
            public string gameMode { get; set; }

            /// <summary>
            /// 游戏模式修饰符列表，通常为空。
            /// </summary>
            public List<string> gameModeMutators { get; set; }

            /// <summary>
            /// 游戏类型，例如 "MATCHED_GAME" 表示匹配游戏。
            /// </summary>
            public string gameType { get; set; }

            /// <summary>
            /// 游戏版本号，例如 "15.18.710.6001"。
            /// </summary>
            public string gameVersion { get; set; }

            /// <summary>
            /// 地图ID，例如 30 代表“终极魔典”模式的地图。
            /// </summary>
            public int mapId { get; set; }

            /// <summary>
            /// 参与者身份信息列表，包含所有玩家的基本信息。
            /// </summary>
            public List<ParticipantIdentitiesItem> participantIdentities { get; set; }

            /// <summary>
            /// 参与者详细信息列表，包含所有玩家的游戏数据。
            /// </summary>
            public List<ParticipantsItem> participants { get; set; }

            /// <summary>
            /// 平台ID，例如 "HN1"。
            /// </summary>
            public string platformId { get; set; }

            /// <summary>
            /// 游戏队列ID，用于区分不同的游戏模式（如排位、匹配等）。
            /// </summary>
            public int queueId { get; set; }

            /// <summary>
            /// 赛季ID。
            /// </summary>
            public int seasonId { get; set; }

            /// <summary>
            /// 队伍信息列表，包含两支队伍的整体统计数据。
            /// </summary>
            public List<TeamsItem> teams { get; set; }
        }

        // <summary>
        /// 自定义Json转换器，用于将JSON中的字符串 "true"/"false" 转换为C#的 bool 类型。
        /// 在您提供的JSON中，很多布尔值是以字符串形式出现的。
        /// </summary>
        public class JsonStringBoolConverter : JsonConverter<bool>
        {
            public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.String)
                {
                    string stringValue = reader.GetString();
                    if (bool.TryParse(stringValue, out bool result))
                    {
                        return result;
                    }
                    // 处理可能的空值或其他情况
                    return false;
                }
                // 如果不是字符串，则按默认方式处理
                return reader.GetBoolean();
            }

            public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
            {
                writer.WriteStringValue(value.ToString().ToLower());
            }
        }
    }
}