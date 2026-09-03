using Newtonsoft.Json;

namespace LOL_GameAssistant.Entity
{
    /// <summary>
    /// 英雄选择阶段会话数据（/lol-champ-select/v1/session）
    /// </summary>
    /// <summary>
    /// 选人阶段会话模型（LCU /lol-champ-select/v1/session）。
    /// </summary>
    public class ChampSelectSession
    {
        /// <summary>轮次动作列表（每轮一个列表，每个列表包含同轮次的所有动作）</summary>
        [JsonProperty("actions")]
        public List<List<ChampSelectAction>> Actions { get; set; } = new();

        /// <summary>己方队伍成员</summary>
        [JsonProperty("myTeam")]
        public List<ChampSelectTeamMember> MyTeam { get; set; } = new();

        /// <summary>对方队伍成员</summary>
        [JsonProperty("theirTeam")]
        public List<ChampSelectTeamMember> TheirTeam { get; set; } = new();

        /// <summary>禁用数据</summary>
        [JsonProperty("bans")]
        public ChampSelectBans? Bans { get; set; }

        /// <summary>本地玩家细胞ID，用于判断轮到谁</summary>
        [JsonProperty("localPlayerCellId")]
        public int LocalPlayerCellId { get; set; }
    }

    /// <summary>
    /// 选人/禁用动作
    /// </summary>
    /// <summary>
    /// 选人动作：禁用/选用英雄。
    /// </summary>
    public class ChampSelectAction
    {
        [JsonProperty("actorCellId")]
        public int ActorCellId { get; set; }

        [JsonProperty("championId")]
        public int ChampionId { get; set; }

        [JsonProperty("completed")]
        public bool Completed { get; set; }

        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("isAllyAction")]
        public bool IsAllyAction { get; set; }

        [JsonProperty("isInProgress")]
        public bool IsInProgress { get; set; }

        [JsonProperty("type")]
        public string? Type { get; set; } // "ban" 或 "pick"
    }

    /// <summary>
    /// 选人阶段的队伍成员
    /// </summary>
    /// <summary>
    /// 选人阶段队伍成员。
    /// </summary>
    public class ChampSelectTeamMember
    {
        [JsonProperty("cellId")]
        public int CellId { get; set; }

        [JsonProperty("championId")]
        public int ChampionId { get; set; }

        [JsonProperty("summonerId")]
        public long SummonerId { get; set; }

        [JsonProperty("puuid")]
        public string? Puuid { get; set; }
    }

    /// <summary>
    /// 禁用汇总数据
    /// </summary>
    /// <summary>
    /// 选人阶段禁用信息。
    /// </summary>
    public class ChampSelectBans
    {
        [JsonProperty("myTeamBans")]
        public List<int> MyTeamBans { get; set; } = new();

        [JsonProperty("theirTeamBans")]
        public List<int> TheirTeamBans { get; set; } = new();
    }
}
