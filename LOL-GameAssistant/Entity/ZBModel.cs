using Newtonsoft.Json;

namespace LOL_GameAssistant.Entity
{
    /// <summary>
    /// 装备信息
    /// </summary>
    public class ZBModel
    {
        [JsonProperty("id")]
        public string? Id { get; set; }

        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonProperty("description")]
        public string? Description { get; set; }

        [JsonProperty("iconPath")]
        public string? IconPath { get; set; }
    }
}
