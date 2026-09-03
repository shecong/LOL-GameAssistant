
namespace LOL_GameAssistant.Entity
{
    /// <summary>
    /// 对局中实时会话模型（LCU /lol-gameflow/v1/session）。
    /// </summary>
    public class GameSessionResponse
    {
        [Newtonsoft.Json.JsonProperty("phase")]
        public string Phase { get; set; } = "";

        [Newtonsoft.Json.JsonProperty("gameData")]
        public GameData GameData { get; set; } = null!;
    }

    /// <summary>
    /// 对局数据：双方队伍成员列表。
    /// </summary>
    public class GameData
    {
        [Newtonsoft.Json.JsonProperty("teamOne")]
        public List<TeamMember> TeamOne { get; set; } = new();

        [Newtonsoft.Json.JsonProperty("teamTwo")]
        public List<TeamMember> TeamTwo { get; set; } = new();
    }

    /// <summary>
    /// 对局中队伍成员信息。
    /// </summary>
    public class TeamMember
    {
        [Newtonsoft.Json.JsonProperty("championId")]
        public int ChampionId { get; set; }

        [Newtonsoft.Json.JsonProperty("lastSelectedSkinIndex")]
        public int LastSelectedSkinIndex { get; set; }

        [Newtonsoft.Json.JsonProperty("profileIconId")]
        public int ProfileIconId { get; set; }

        [Newtonsoft.Json.JsonProperty("puuid")]
        public string Puuid { get; set; } = "";

        [Newtonsoft.Json.JsonProperty("selectedPosition")]
        public string SelectedPosition { get; set; } = "";

        [Newtonsoft.Json.JsonProperty("selectedRole")]
        public string SelectedRole { get; set; } = "";

        [Newtonsoft.Json.JsonProperty("summonerId")]
        public long SummonerId { get; set; }

        [Newtonsoft.Json.JsonProperty("summonerInternalName")]
        public string SummonerInternalName { get; set; } = "";

        [Newtonsoft.Json.JsonProperty("summonerName")]
        public string SummonerName { get; set; } = "";

        [Newtonsoft.Json.JsonProperty("teamOwner")]
        public bool TeamOwner { get; set; }

        [Newtonsoft.Json.JsonProperty("teamParticipantId")]
        public int TeamParticipantId { get; set; }
    }
}
