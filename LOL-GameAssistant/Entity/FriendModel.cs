using Newtonsoft.Json;

namespace LOL_GameAssistant.Entity
{
    /// <summary>
    /// LOL 客户端好友信息（LCU /lol-chat/v1/friends）。
    /// </summary>
    public sealed class FriendModel
    {
        [JsonProperty("availability")]
        public string? Availability { get; set; }

        [JsonProperty("displayName")]
        public string? DisplayName { get; set; }

        [JsonProperty("gameName")]
        public string? GameName { get; set; }

        [JsonProperty("tagLine")]
        public string? TagLine { get; set; }

        [JsonProperty("puuid")]
        public string? Puuid { get; set; }

        [JsonProperty("summonerName")]
        public string? SummonerName { get; set; }

        [JsonProperty("statusMessage")]
        public string? StatusMessage { get; set; }

        [JsonProperty("note")]
        public string? Note { get; set; }

        [JsonProperty("icon")]
        public int Icon { get; set; }

        [JsonProperty("lol")]
        public FriendLolInfo? Lol { get; set; }
    }

    /// <summary>
    /// 好友对象中的 LOL 状态信息。
    /// </summary>
    public sealed class FriendLolInfo
    {
        [JsonProperty("gameStatus")]
        public string? GameStatus { get; set; }

        [JsonProperty("gameId")]
        public string? GameId { get; set; }

        [JsonProperty("gameQueueType")]
        public string? GameQueueType { get; set; }

        [JsonProperty("icon")]
        public int Icon { get; set; }

        [JsonProperty("level")]
        public int Level { get; set; }
    }
}
