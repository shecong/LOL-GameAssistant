using System.Globalization;

namespace LOL_GameAssistant.Domain.MatchAnalysis;

/// <summary>不添加标题或补齐的聊天宽度标尺，便于按截图中的编号确定实际换行位置。</summary>
public static class GameChatWidthRuler
{
    public static IReadOnlyList<string> BuildMessages()
    {
        var numbers = Enumerable.Range(1, 60).Select(value => value.ToString("D2", CultureInfo.InvariantCulture)).ToArray();
        var wide = numbers.Select(number => string.Concat(number.Select(character => (char)(character + 0xFEE0)))).ToArray();
        return [string.Concat(wide), string.Concat(numbers), string.Join('\u3000', wide.Take(40))];
    }
}
