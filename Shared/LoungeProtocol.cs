namespace LOL.Lounge;

// Shared wire contract; deliberately independent of WinForms and LCU credentials.
public sealed record LoungePresence(string Puuid, string Name, string Platform, string Region,
    string Status, bool ShareActivity = true, string Rank = "");
public sealed record LoungeMember(string Id, string Name, string Region, string Status, string Rank = "");
public sealed record LoungeMessage(long Id, string ClientId, string SenderId, string Name,
    string Region, string Kind, string Text, DateTimeOffset SentAt);
public sealed record LoungeSnapshot(LoungeMessage[] Messages, LoungeMember[] Members, long[]? DeletedIds = null,
    int ProtocolVersion = 1, LoungeRecruitmentState[]? Recruitments = null);
public sealed record LoungeSend(string ClientId, string Text, string Kind = "chat", string[]? Mentions = null,
    LoungeRecruitment? Recruitment = null);
public sealed record LoungeRecruitment(string Region, int QueueId, string Rank, string Position, int Needed, string Note = "");
public sealed record LoungeRecruitmentState(long MessageId, string OwnerId, string OwnerName,
    LoungeRecruitment Recruitment, LoungeMember[] Participants, bool Closed, DateTimeOffset ExpiresAt, int Revision = 1)
{
    public string Status => Closed ? "已结束" : ExpiresAt <= DateTimeOffset.UtcNow ? "已过期" :
        Participants.Length >= Recruitment.Needed ? "已满员" : "招募中";
}
public sealed record LoungeChatContent(string Text, string[] Mentions);
public sealed record LoungeHighlight(long GameId, string Platform, string Puuid, int QueueId,
    DateTimeOffset FinishedAt, string Kind, string Detail = "");
public sealed record LoungeVictory(long GameId, string Platform, string Puuid, int QueueId,
    DateTimeOffset FinishedAt, string[]? PartyNames = null);

public static class LoungeRules
{
    public const int ProtocolVersion = 3;
    public const int MaxTextLength = 500;
    public static readonly IReadOnlyDictionary<int, string> Modes = new Dictionary<int, string>
    {
        [400] = "匹配", [420] = "单双排", [430] = "匹配", [440] = "灵活组排",
        [450] = "极地大乱斗", [480] = "快速模式", [490] = "快速模式",
        [900] = "无限火力", [1020] = "克隆大作战", [1300] = "极限闪击",
        [1400] = "终极魔典", [1900] = "无限乱斗", [2400] = "海克斯大乱斗", [3270] = "海克斯大乱斗"
    };

    public static string Clean(string? value, int maximum) =>
        new((value ?? "").Where(c => !char.IsControl(c) && c != '\u202E').Take(maximum).ToArray());

    public static string PlayerId(LoungePresence presence) => string.IsNullOrWhiteSpace(presence.Puuid)
        ? "" : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(presence.Platform + ":" + presence.Puuid)));

    public static bool IsRecent(DateTimeOffset finishedAt, DateTimeOffset now) =>
        finishedAt >= now.AddMinutes(-10) && finishedAt <= now.AddMinutes(1);
}
