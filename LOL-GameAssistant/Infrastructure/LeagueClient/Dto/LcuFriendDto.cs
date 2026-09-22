using Newtonsoft.Json;

namespace LOL_GameAssistant.Infrastructure.LeagueClient.Dto;

/// <summary>LCU /lol-chat/v1/friends 的传输对象，仅限基础设施层使用。</summary>
internal sealed class LcuFriendDto
{
    [JsonProperty("availability")] public string? Availability { get; set; }
    [JsonProperty("displayName")] public string? DisplayName { get; set; }
    [JsonProperty("gameName")] public string? GameName { get; set; }
    [JsonProperty("tagLine")] public string? TagLine { get; set; }
    [JsonProperty("puuid")] public string? Puuid { get; set; }
    [JsonProperty("summonerName")] public string? SummonerName { get; set; }
    [JsonProperty("statusMessage")] public string? StatusMessage { get; set; }
    [JsonProperty("note")] public string? Note { get; set; }
    [JsonProperty("icon")] public int Icon { get; set; }
    [JsonProperty("lol")] public LcuFriendLolDto? Lol { get; set; }
}

internal sealed class LcuFriendLolDto
{
    [JsonProperty("gameId")] public string? GameId { get; set; }
    [JsonProperty("gameQueueType")] public string? GameQueueType { get; set; }
    [JsonProperty("icon")] public int Icon { get; set; }
}