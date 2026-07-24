namespace LOL_GameAssistant.Entity
{
    public class PlayerModel
    {
        public class RerollPoints
        {
            /// <summary>
            ///
            /// </summary>
            public int currentPoints { get; set; }

            /// <summary>
            ///
            /// </summary>
            public int maxRolls { get; set; }

            /// <summary>
            ///
            /// </summary>
            public int numberOfRolls { get; set; }

            /// <summary>
            ///
            /// </summary>
            public int pointsCostToRoll { get; set; }

            /// <summary>
            ///
            /// </summary>
            public int pointsToReroll { get; set; }
        }

        /// <summary>
        /// 召唤师/玩家信息
        /// </summary>
        public class Plyaer
        {
            [Newtonsoft.Json.JsonProperty("accountId")]
            public long? AccountId { get; set; }

            [Newtonsoft.Json.JsonProperty("displayName")]
            public string? DisplayName { get; set; }

            [Newtonsoft.Json.JsonProperty("gameName")]
            public string? GameName { get; set; }

            [Newtonsoft.Json.JsonProperty("internalName")]
            public string? InternalName { get; set; }

            [Newtonsoft.Json.JsonProperty("nameChangeFlag")]
            public string? NameChangeFlag { get; set; }

            [Newtonsoft.Json.JsonProperty("percentCompleteForNextLevel")]
            public int PercentCompleteForNextLevel { get; set; }

            [Newtonsoft.Json.JsonProperty("privacy")]
            public string? Privacy { get; set; }

            [Newtonsoft.Json.JsonProperty("profileIconId")]
            public int ProfileIconId { get; set; }

            [Newtonsoft.Json.JsonProperty("puuid")]
            public string? Puuid { get; set; }

            [Newtonsoft.Json.JsonProperty("rerollPoints")]
            public RerollPoints? RerollPoints { get; set; }

            [Newtonsoft.Json.JsonProperty("summonerId")]
            public long SummonerId { get; set; }

            [Newtonsoft.Json.JsonProperty("summonerLevel")]
            public int SummonerLevel { get; set; }

            [Newtonsoft.Json.JsonProperty("tagLine")]
            public string? TagLine { get; set; }

            [Newtonsoft.Json.JsonProperty("unnamed")]
            public string? Unnamed { get; set; }

            [Newtonsoft.Json.JsonProperty("xpSinceLastLevel")]
            public int XpSinceLastLevel { get; set; }

            [Newtonsoft.Json.JsonProperty("xpUntilNextLevel")]
            public int XpUntilNextLevel { get; set; }
        }
    }
}