namespace LOL_GameAssistant.Model
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

        public class Plyaer
        {
            /// <summary>
            ///
            /// </summary>
            public String? accountId { get; set; }

            /// <summary>
            ///
            /// </summary>
            public String? displayName { get; set; }

            /// <summary>
            /// 封觉九灵奇楠天
            /// </summary>
            public string gameName { get; set; }

            /// <summary>
            ///
            /// </summary>
            public string internalName { get; set; }

            /// <summary>
            ///
            /// </summary>
            public string nameChangeFlag { get; set; }

            /// <summary>
            ///
            /// </summary>
            public String? percentCompleteForNextLevel { get; set; }

            /// <summary>
            ///
            /// </summary>
            public string privacy { get; set; }

            /// <summary>
            ///
            /// </summary>
            public String? profileIconId { get; set; }

            /// <summary>
            ///
            /// </summary>
            public string puuid { get; set; }

            /// <summary>
            ///
            /// </summary>
            public RerollPoints rerollPoints { get; set; }

            /// <summary>
            ///
            /// </summary>
            public String? summonerId { get; set; }

            /// <summary>
            ///
            /// </summary>
            public String? summonerLevel { get; set; }

            /// <summary>
            ///
            /// </summary>
            public string tagLine { get; set; }

            /// <summary>
            ///
            /// </summary>
            public string unnamed { get; set; }

            /// <summary>
            ///
            /// </summary>
            public int xpSinceLastLevel { get; set; }

            /// <summary>
            ///
            /// </summary>
            public int xpUntilNextLevel { get; set; }
        }
    }
}