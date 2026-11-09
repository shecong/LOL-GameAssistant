using System.Text.RegularExpressions;

namespace LOL_GameAssistant.Helper;

/// <summary>沿用覆盖层文本通道；制表符后为悬停说明，不参与可见列与排序。</summary>
internal static class AugmentOverlayText
{
    internal static string Format(string label, string? description)
    {
        string text = Regex.Replace(AssetDetailToolTip.PlainText(description), @"\s+", " ").Trim();
        if (text.Length == 0) text = UiLanguage.IsEnglish ? "Description unavailable" : "暂未获取到该强化的效果说明";
        // 每页至多12条；限制单条说明，确保 UTF-8 内容完整放入固定大小的共享内存。
        if (text.Length > 650)
        {
            int length = char.IsHighSurrogate(text[649]) ? 649 : 650;
            text = text[..length] + "…";
        }
        return label.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ') + "\t" + text;
    }
}
