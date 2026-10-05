namespace LOL_GameAssistant.Domain.MatchAnalysis;

/// <summary>各档位独立随机选句，连续选句不重复；自定义每行一句，空词库使用内置文案。</summary>
public static class GameKdaRemarkLibrary
{
    private static readonly object Sync = new();
    private static readonly Dictionary<RecentPerformanceLabel, string> Last = new();
    private static readonly Dictionary<RecentPerformanceLabel, string[]> BuiltIn = new()
    {
        [RecentPerformanceLabel.Upper] = [
            "这KDA一摆，别人负责参与，你负责收尾。", "别人来凑人数，你来提高平均水平。",
            "队友的容错率，看起来是你给的。", "这份战绩，建议队友先把感谢词准备好。",
            "别人研究怎么赢，你研究怎么赢得漂亮。", "这KDA，队友可以少找两个借口了。",
            "这局的主角候选名单，先给你留个位置。", "这份成绩单，确实不用靠嗓门证明实力。"],
        [RecentPerformanceLabel.Medium] = [
            "主打一个到场，离带飞还差点意思。", "这KDA，进步空间比高光镜头更明显。",
            "成绩不算难看，惊喜也确实不多。", "存在感刚好够签到，离主角还有一段路。",
            "这份战绩，属于队友不敢放心也不好意思骂。", "想当大腿的心有了，战绩还在排队。",
            "不至于劝退，但也先别急着喊包赢。", "这KDA，建议先把稳定发挥练成常驻技能。"],
        [RecentPerformanceLabel.Lower] = [
            "带飞没看出来，给对面加戏倒是挺熟练。", "战绩这么谦虚，嘴上就别太有冠军气质。",
            "这KDA，队友的血压怕是比胜率更稳定。", "对面最想保留的，可能就是你的发挥。",
            "这份成绩单，甩锅之前建议先看一眼。", "战绩负责制造悬念，队友负责解决问题。",
            "这KDA，建议少研究谁的锅，多研究下一波怎么活。", "想带飞没问题，先别让队友负责抢救。"],
        [RecentPerformanceLabel.Human] = [
            "复活倒计时都快成常驻技能了。", "泉水的门禁记录，怕是比高光集锦更厚。",
            "这KDA，对面看到名字都像看到福利通知。", "队友打的是比赛，你打的是他们的耐心。",
            "别急着指挥全队，先把自己从复活区指挥出来。", "这战绩要是能兑换积分，对面早就满额了。",
            "这KDA，嘴硬之前先给成绩单放个假。", "先别谈战术高度，活着参与才是第一步。"]
    };

    /// <summary>优先使用用户文案，在对应档位随机选句，并避免连续两次重复。</summary>
    public static string Select(RecentPerformanceLabel label, IReadOnlyDictionary<string, string>? custom = null)
    {
        string[] choices = custom != null && custom.TryGetValue(label.ToString(), out string? text)
            ? text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => line.Length <= 200).Distinct(StringComparer.Ordinal).ToArray()
            : [];
        if (choices.Length == 0) choices = BuiltIn.GetValueOrDefault(label, ["数据不足，暂不测评。"]);
        lock (Sync)
        {
            Last.TryGetValue(label, out string? previous);
            string[] candidates = choices.Length > 1 ? choices.Where(choice => choice != previous).ToArray() : choices;
            string selected = candidates[Random.Shared.Next(candidates.Length)];
            Last[label] = selected;
            return selected;
        }
    }
}
