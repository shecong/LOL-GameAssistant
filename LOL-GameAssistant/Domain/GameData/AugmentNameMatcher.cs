using System.Text;

namespace LOL_GameAssistant.Domain.GameData;

/// <summary>在固定的海克斯名称表中容错匹配 OCR 结果。</summary>
public static class AugmentNameMatcher
{
    /// <summary>将识别文本与强化名称候选进行匹配。</summary>
    public static IReadOnlyList<int> Match(string recognizedText, IReadOnlyDictionary<int, string> names, int limit = 3)
        => Match(recognizedText, names.Select(item => (item.Key, item.Value)), limit);

    /// <summary>将识别文本与强化名称候选进行匹配。</summary>
    public static IReadOnlyList<int> Match(string recognizedText, IEnumerable<(int Id, string Name)> names, int limit = 3)
        => RankMatches(recognizedText, names).Take(Math.Max(0, limit)).Select(item => item.Id).ToArray();

    /// <summary>单张卡片只接受明确的最佳候选，避免把相似名称中的任意一个当成结果。</summary>
    public static AugmentNameMatch? MatchCard(string recognizedText, IEnumerable<(int Id, string Name)> names)
    {
        var ranked = RankMatches(recognizedText, names).Take(2).ToArray();
        if (ranked.Length == 0 || (ranked.Length > 1 && ranked[0].Score - ranked[1].Score < 0.08))
            return null;
        return ranked[0];
    }

    /// <summary>计算候选名称与识别文本的相似度并排序。</summary>
    private static IEnumerable<AugmentNameMatch> RankMatches(string recognizedText, IEnumerable<(int Id, string Name)> names)
    {
        // The catalog contains both Arena IDs and Mayhem IDs for some identical names.
        // This scanner is used for Mayhem, where the 1000+ IDs match the stats feed.
        var candidates = names.Select(item => (item.Id, Normalized: Normalize(item.Name)))
            .Where(item => item.Normalized.Length >= 2)
            .GroupBy(item => item.Normalized, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.Id >= 1000)
                .ThenByDescending(item => item.Id).First())
            .ToArray();
        string[] lines = (recognizedText ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        string[] normalizedLines = lines.Select(Normalize).Where(line => line.Length > 0).ToArray();
        var result = new List<(int Id, double Score)>();
        // Keep one-character fragments and join up to three lines, including long English titles.
        var readings = new HashSet<string>(normalizedLines, StringComparer.Ordinal);
        int maxNameLength = candidates.Select(item => item.Normalized.Length).DefaultIfEmpty(0).Max();
        for (int index = 0; index < normalizedLines.Length; index++)
        {
            string joined = normalizedLines[index];
            for (int next = index + 1; next < Math.Min(index + 3, normalizedLines.Length); next++)
            {
                // Do not turn isolated Latin OCR noise into a plausible English title.
                string fragment = normalizedLines[next];
                if (HasChinese(joined) != HasChinese(fragment) ||
                    (!HasChinese(joined) && (joined.Length < 3 || fragment.Length < 3))) break;
                joined += fragment;
                if (joined.Length > maxNameLength) break;
                readings.Add(joined);
            }
        }
        foreach (string line in readings.Where(line => line.Length >= 2))
        {
            foreach ((int id, string name) in candidates)
            {
                bool chinese = HasChinese(name);
                if (HasChinese(line) != chinese) continue;
                if (line != name && !chinese && line.Length < 4) continue;
                int distance = Distance(line, name);
                // Short Chinese titles cannot safely tolerate arbitrary substitutions.
                // Still allow one inserted/deleted character, e.g. 夺人金 -> 夺金.
                if (chinese && name.Length <= 3 && line != name &&
                    !(Math.Abs(line.Length - name.Length) == 1 && distance == 1)) continue;
                double score = line == name ? 1 :
                    line.Contains(name, StringComparison.Ordinal) ? 0.9 :
                    name.Contains(line, StringComparison.Ordinal) && line.Length >= (chinese ? 3 : 6) &&
                        line.Length >= name.Length * 0.75 ? 0.82 :
                    1d - (double)distance / Math.Max(line.Length, name.Length);
                double threshold = chinese && name.Length <= 3 ? 0.66 : 0.75;
                if (score >= threshold) result.Add((id, score));
            }
        }
        return result.OrderByDescending(item => item.Score)
            .DistinctBy(item => item.Id).Select(item => new AugmentNameMatch(item.Id, item.Score));
    }

    private static bool HasChinese(string text) => text.Any(c =>
        c is >= '\u3400' and <= '\u4dbf' or >= '\u4e00' and <= '\u9fff' or >= '\uf900' and <= '\ufaff');

    /// <summary>将配置或文本输入规范化为后续处理使用的形式。</summary>
    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (char character in text.Normalize(NormalizationForm.FormKC))
            if (char.IsLetterOrDigit(character)) builder.Append(char.ToLowerInvariant(character));
        return builder.ToString();
    }

    /// <summary>计算两个规范化文本的编辑距离。</summary>
    private static int Distance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (int i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= right.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return previous[right.Length];
    }
}

/// <summary>强化名称匹配的候选标识、名称和相似度。</summary>
public sealed record AugmentNameMatch(int Id, double Score);
