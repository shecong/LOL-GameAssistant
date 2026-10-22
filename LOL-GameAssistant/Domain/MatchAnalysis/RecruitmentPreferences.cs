namespace LOL_GameAssistant.Domain.MatchAnalysis;

public static class RecruitmentPreferences
{
    public static readonly string[] Styles = ["轻松娱乐", "认真上分", "练习英雄"];
    public static string Compose(string style, bool voice, string time, string note)
    {
        if (!Styles.Contains(style)) throw new ArgumentException("请选择有效的组队目的");
        string available = new((time ?? "").Where(c => !char.IsControl(c) && c != '】' && c != '【').Take(30).ToArray());
        string value = $"【{style}｜{(voice ? "愿意语音" : "无需语音")}{(available.Length > 0 ? "｜" + available : "")}】" + note.Trim();
        if (value.Length > 120) throw new ArgumentException("组队偏好与说明合计不能超过 120 字，请缩短补充说明。");
        return value;
    }
    public static string? Style(string? note) => Styles.FirstOrDefault(style => (note ?? "").StartsWith("【" + style + "｜", StringComparison.Ordinal));
}
