using Newtonsoft.Json;

namespace LOL_GameAssistant.Infrastructure.LeagueClient.Dto;

/// <summary>LCU 召唤师资料传输对象，仅处理本次业务实际需要的字段。</summary>
internal sealed class LcuPlayerProfileDto
{
    [JsonProperty("puuid")] public string? Puuid { get; set; }
    [JsonProperty("gameName")] public string? GameName { get; set; }
    [JsonProperty("tagLine")] public string? TagLine { get; set; }
    [JsonProperty("profileIconId")] public string? ProfileIconId { get; set; }
    [JsonProperty("summonerLevel")] public string? SummonerLevel { get; set; }
    [JsonProperty("xpSinceLastLevel")] public int XpSinceLastLevel { get; set; }
    [JsonProperty("xpUntilNextLevel")] public int XpUntilNextLevel { get; set; }
}