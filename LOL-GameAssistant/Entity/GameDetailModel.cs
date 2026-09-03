using Newtonsoft.Json;

namespace LOL_GameAssistant.Entity
{
    /// <summary>
    /// 单场对局详情 JSON 模型（LCU /lol-match-history/v1/games/{gameId}）。
    /// </summary>
    public class GameDetailModel
    {
        public class Player
        {
            public int accountId { get; set; }
            public int currentAccountId { get; set; }
            public string currentPlatformId { get; set; } = "";
            public string gameName { get; set; } = "";
            public string matchHistoryUri { get; set; } = "";
            public string platformId { get; set; } = "";
            public int profileIcon { get; set; }
            public string puuid { get; set; } = "";
            public long summonerId { get; set; }
            public string summonerName { get; set; } = "";
            public string tagLine { get; set; } = "";
        }

        public class ParticipantIdentitiesItem
        {
            public int participantId { get; set; }
            public Player player { get; set; } = null!;
        }

        public class Stats
        {
            public int assists { get; set; }
            public bool causedEarlySurrender { get; set; }
            public int champLevel { get; set; }
            public int combatPlayerScore { get; set; }
            public int damageDealtToObjectives { get; set; }
            public int damageDealtToTurrets { get; set; }
            public int damageSelfMitigated { get; set; }
            public int deaths { get; set; }
            public int doubleKills { get; set; }
            public bool earlySurrenderAccomplice { get; set; }
            public bool firstBloodAssist { get; set; }
            public bool firstBloodKill { get; set; }
            public bool firstInhibitorAssist { get; set; }
            public bool firstInhibitorKill { get; set; }
            public bool firstTowerAssist { get; set; }
            public bool firstTowerKill { get; set; }
            public bool gameEndedInEarlySurrender { get; set; }
            public bool gameEndedInSurrender { get; set; }
            public int goldEarned { get; set; }
            public int goldSpent { get; set; }
            public int inhibitorKills { get; set; }
            public int item0 { get; set; }
            public int item1 { get; set; }
            public int item2 { get; set; }
            public int item3 { get; set; }
            public int item4 { get; set; }
            public int item5 { get; set; }
            public int item6 { get; set; }
            public int killingSprees { get; set; }
            public int kills { get; set; }
            public int largestCriticalStrike { get; set; }
            public int largestKillingSpree { get; set; }
            public int largestMultiKill { get; set; }
            public int longestTimeSpentLiving { get; set; }
            public int magicDamageDealt { get; set; }
            public int magicDamageDealtToChampions { get; set; }
            public int magicalDamageTaken { get; set; }
            public int neutralMinionsKilled { get; set; }
            public int neutralMinionsKilledEnemyJungle { get; set; }
            public int neutralMinionsKilledTeamJungle { get; set; }
            public int objectivePlayerScore { get; set; }
            public int participantId { get; set; }
            public int pentaKills { get; set; }
            public int perk0 { get; set; }
            public int perk0Var1 { get; set; }
            public int perk0Var2 { get; set; }
            public int perk0Var3 { get; set; }
            public int perk1 { get; set; }
            public int perk1Var1 { get; set; }
            public int perk1Var2 { get; set; }
            public int perk1Var3 { get; set; }
            public int perk2 { get; set; }
            public int perk2Var1 { get; set; }
            public int perk2Var2 { get; set; }
            public int perk2Var3 { get; set; }
            public int perk3 { get; set; }
            public int perk3Var1 { get; set; }
            public int perk3Var2 { get; set; }
            public int perk3Var3 { get; set; }
            public int perk4 { get; set; }
            public int perk4Var1 { get; set; }
            public int perk4Var2 { get; set; }
            public int perk4Var3 { get; set; }
            public int perk5 { get; set; }
            public int perk5Var1 { get; set; }
            public int perk5Var2 { get; set; }
            public int perk5Var3 { get; set; }
            public int perkPrimaryStyle { get; set; }
            public int perkSubStyle { get; set; }
            public int physicalDamageDealt { get; set; }
            public int physicalDamageDealtToChampions { get; set; }
            public int physicalDamageTaken { get; set; }
            public int playerAugment1 { get; set; }
            public int playerAugment2 { get; set; }
            public int playerAugment3 { get; set; }
            public int playerAugment4 { get; set; }
            public int playerAugment5 { get; set; }
            public int playerAugment6 { get; set; }
            public int playerScore0 { get; set; }
            public int playerScore1 { get; set; }
            public int playerScore2 { get; set; }
            public int playerScore3 { get; set; }
            public int playerScore4 { get; set; }
            public int playerScore5 { get; set; }
            public int playerScore6 { get; set; }
            public int playerScore7 { get; set; }
            public int playerScore8 { get; set; }
            public int playerScore9 { get; set; }
            public int playerSubteamId { get; set; }
            public int quadraKills { get; set; }
            public int sightWardsBoughtInGame { get; set; }
            public int subteamPlacement { get; set; }
            public bool teamEarlySurrendered { get; set; }
            public int timeCCingOthers { get; set; }
            public int totalDamageDealt { get; set; }
            public int totalDamageDealtToChampions { get; set; }
            public int totalDamageTaken { get; set; }
            public int totalHeal { get; set; }
            public int totalMinionsKilled { get; set; }
            public int totalPlayerScore { get; set; }
            public int totalScoreRank { get; set; }
            public int totalTimeCrowdControlDealt { get; set; }
            public int totalUnitsHealed { get; set; }
            public int tripleKills { get; set; }
            public int trueDamageDealt { get; set; }
            public int trueDamageDealtToChampions { get; set; }
            public int trueDamageTaken { get; set; }
            public int turretKills { get; set; }
            public int unrealKills { get; set; }
            public int visionScore { get; set; }
            public int visionWardsBoughtInGame { get; set; }
            public int wardsKilled { get; set; }
            public int wardsPlaced { get; set; }
            /// <summary>
            /// LCU 通常返回布尔值，也兼容旧数据中的 Win/Fail 字符串或 1/0。
            /// </summary>
            public object? win { get; set; }
        }

        public class Timeline
        {
            public object creepsPerMinDeltas { get; set; } = new();
            public object csDiffPerMinDeltas { get; set; } = new();
            public object damageTakenDiffPerMinDeltas { get; set; } = new();
            public object damageTakenPerMinDeltas { get; set; } = new();
            public object goldPerMinDeltas { get; set; } = new();
            public string lane { get; set; } = "";
            public string role { get; set; } = "";
            public object xpDiffPerMinDeltas { get; set; } = new();
            public object xpPerMinDeltas { get; set; } = new();
        }

        public class ParticipantsItem
        {
            public int championId { get; set; }
            public string highestAchievedSeasonTier { get; set; } = "";
            public int participantId { get; set; }
            public string Spell1Id { get; set; } = "";
            public string Spell2Id { get; set; } = "";
            public Stats stats { get; set; } = null!;
            public int teamId { get; set; }
            public Timeline timeline { get; set; } = null!;
        }

        public class BansItem
        {
            public int championId { get; set; }
            public int pickTurn { get; set; }
        }

        public class TeamsItem
        {
            public List<BansItem> bans { get; set; } = new();
            public int baronKills { get; set; }
            public int dominionVictoryScore { get; set; }
            public int dragonKills { get; set; }
            public bool firstBaron { get; set; }
            public bool firstBlood { get; set; }
            public bool firstDargon { get; set; }
            public bool firstInhibitor { get; set; }
            public bool firstTower { get; set; }
            public int hordeKills { get; set; }
            public int inhibitorKills { get; set; }
            public int riftHeraldKills { get; set; }
            public int teamId { get; set; }
            public int towerKills { get; set; }
            public int vilemawKills { get; set; }
            public string win { get; set; } = "";
        }

        public class GameInfo
        {
            public string endOfGameResult { get; set; } = "";
            public long gameCreation { get; set; }
            public string gameCreationDate { get; set; } = "";
            public int gameDuration { get; set; }
            public long gameId { get; set; }
            public string _gameMode { get; set; } = "";
            public string gameMode { get; set; } = "";
            public List<string> gameModeMutators { get; set; } = new();
            public string gameType { get; set; } = "";
            public string gameVersion { get; set; } = "";
            public int mapId { get; set; }
            public List<ParticipantIdentitiesItem> participantIdentities { get; set; } = new();
            public List<ParticipantsItem> participants { get; set; } = new();
            public string platformId { get; set; } = "";
            public string _queueId { get; set; } = "";
            public string queueId { get; set; } = "";
            public int seasonId { get; set; }
            public List<TeamsItem> teams { get; set; } = new();
        }
    }
}
