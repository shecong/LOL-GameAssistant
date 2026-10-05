namespace LOL_GameAssistant.Entity
{
    /// <summary>召唤师技能资源条目，保存名称和图标路径。</summary>
    public class JNModel
    {
        public String id { get; set; } = "";
        public String name { get; set; } = "";

        public String description { get; set; } = "";

        public String iconPath { get; set; } = "";
    }
}