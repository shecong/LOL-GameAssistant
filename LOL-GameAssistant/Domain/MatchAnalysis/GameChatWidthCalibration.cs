using System.Text;

namespace LOL_GameAssistant.Domain.MatchAnalysis;

public sealed record GameChatLineWidths(int FirstLine, int ContinuationLine)
{
    public static GameChatLineWidths Default { get; } = new(30, 70);
    public bool IsValid => FirstLine is >= 8 and <= 240 && ContinuationLine >= FirstLine && ContinuationLine <= 240;
}

/// <summary>仅接受同一随机标尺中连续且等宽的完整续行，拒绝缺行和 OCR 猜测。</summary>
public static class GameChatWidthCalibration
{
    public static string CreateProbe() => string.Concat(Enumerable.Range(0, 240)
        .Select(_ => (char)('０' + System.Security.Cryptography.RandomNumberGenerator.GetInt32(10))));

    public static GameChatLineWidths? Measure(string probe, IReadOnlyList<string> lines)
    {
        string expected = Normalize(probe);
        var spans = lines.Select(line => FindSpan(expected, Normalize(line))).ToArray();
        for (int i = 0; i + 3 < spans.Length; i++)
        {
            var first = spans[i];
            var second = spans[i + 1];
            var third = spans[i + 2];
            var fourth = spans[i + 3];
            if (first.Start != 0 || second.Start != first.Length ||
                third.Start != second.Start + second.Length ||
                fourth.Start != third.Start + third.Length || second.Length != third.Length) continue;
            var widths = new GameChatLineWidths(first.Length * 2, second.Length * 2);
            if (widths.IsValid) return widths;
        }
        return null;
    }

    private static string Normalize(string value) => string.Concat(value.Normalize(NormalizationForm.FormKC)
        .Where(char.IsAsciiDigit));

    private static (int Start, int Length) FindSpan(string probe, string text)
    {
        // The first row may include timestamp/channel/sender digits. Only its suffix is the probe.
        for (int offset = 0; offset <= text.Length - 8; offset++)
        {
            string suffix = text[offset..];
            int start = probe.IndexOf(suffix, StringComparison.Ordinal);
            if (start >= 0 && probe.LastIndexOf(suffix, StringComparison.Ordinal) == start)
                return (start, suffix.Length);
        }
        return (-1, 0);
    }
}
