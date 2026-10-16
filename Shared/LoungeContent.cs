using System.Text.Json;

namespace LOL.Lounge;

public sealed record LoungeSticker(string Id, string Caption, string Face, string Color);

public static class LoungeContent
{
    public static readonly LoungeSticker[] Stickers =
    [
        new("nice", "这波漂亮", "happy", "#3B82F6"), new("go", "冲冲冲！", "excited", "#F59E0B"),
        new("gg", "好队友，赞！", "love", "#EC4899"), new("cry", "差一点就赢了", "cry", "#8B5CF6"),
        new("wait", "等我上线", "wait", "#14B8A6"), new("win", "今晚连胜！", "excited", "#EF4444"),
        new("question", "发生什么事了？", "question", "#64748B"), new("rest", "休息一把", "sleep", "#6366F1")
    ];
    public static readonly string[] Positions = ["任意位置", "上单", "打野", "中单", "下路", "辅助"];
    public static readonly string[] Ranks = ["不限段位", "黑铁", "青铜", "白银", "黄金", "铂金", "翡翠", "钻石", "大师", "宗师", "王者"];
    public static LoungeSticker? Sticker(string id) => Stickers.FirstOrDefault(s => s.Id == id);
    public static (string Kind, string Text) ValidateSend(LoungeSend request)
    {
        if (request == null || !Guid.TryParseExact(request.ClientId, "N", out _))
            throw new ArgumentException("消息编号无效。");
        switch (request.Kind)
        {
            case "chat":
                if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > LoungeRules.MaxTextLength)
                    throw new ArgumentException("请输入 1–500 字的消息。");
                string text = LoungeRules.Clean(request.Text, LoungeRules.MaxTextLength).Trim();
                if (text.Length == 0) throw new ArgumentException("消息不能为空。");
                var mentions = (request.Mentions ?? []).Distinct().ToArray();
                if (mentions.Length > 5 || mentions.Any(id => id == null || id.Length != 64 || !id.All(Uri.IsHexDigit)))
                    throw new ArgumentException("每条消息最多提及 5 位有效成员。");
                return mentions.Length == 0 ? ("chat", text) : ("mention", Serialize(new LoungeChatContent(text, mentions)));
            case "sticker":
                if (Sticker(request.Text) == null) throw new ArgumentException("只能发送内置表情包。");
                return ("sticker", request.Text);
            case "recruit":
                var r = request.Recruitment;
                if (r == null || string.IsNullOrWhiteSpace(r.Region) || r.Region.Length > 40 ||
                    !LoungeRules.Modes.ContainsKey(r.QueueId) || !Ranks.Contains(r.Rank) || !Positions.Contains(r.Position) ||
                    r.Needed is < 1 or > 4 || (r.Note?.Length ?? 0) > 120)
                    throw new ArgumentException("请检查招募的大区、模式、段位、位置和人数。");
                string region = LoungeRules.Clean(r.Region, 40).Trim();
                if (region.Length == 0) throw new ArgumentException("请选择有效的招募大区。");
                return ("recruit", Serialize(r with { Region = region, Note = LoungeRules.Clean(r.Note, 120).Trim() }));
            default: throw new ArgumentException("不支持该消息类型；只允许文字、招募卡片和内置表情包。");
        }
    }
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value);
    public static T? Parse<T>(string text)
    {
        try { return JsonSerializer.Deserialize<T>(text); }
        catch (JsonException) { return default; }
    }
    public static string[] Mentions(LoungeMessage message) => message.Kind == "mention"
        ? Parse<LoungeChatContent>(message.Text)?.Mentions ?? [] : [];
    public static string Display(LoungeMessage message) => message.Kind switch
    {
        "sticker" => Sticker(message.Text) is { } sticker ? "[表情包] " + sticker.Caption : "[未知表情包]",
        "mention" => Parse<LoungeChatContent>(message.Text)?.Text ?? "[消息格式无效]",
        "recruit" => Parse<LoungeRecruitment>(message.Text) is { } r
            ? $"🎮 开黑招募 · {r.Region} · {LoungeRules.Modes.GetValueOrDefault(r.QueueId, "未知模式")}\n{r.Rank} · 缺{r.Needed}人 · {r.Position}\n{r.Note}\n想加入？右键这条消息，复制游戏 ID 或 @发起人。"
            : "[招募信息无效]",
        _ => message.Text
    };
}
