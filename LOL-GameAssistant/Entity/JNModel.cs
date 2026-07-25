using Newtonsoft.Json;

namespace LOL_GameAssistant.Entity
{
    /// <summary>
    /// 召唤师技能/装备基础数据模型
    /// </summary>
    public class JNModel
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
