using LOL_GameAssistant.Application.LeagueClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>发送用户选定的一条短句到当前客户端群聊。</summary>
public sealed class LcuQuickShoutService
{
    private readonly ILcuRequestSender _lcu;

    public LcuQuickShoutService(ILcuRequestSender lcu) => _lcu = lcu;

    public async Task<string> SendAsync(string phrase, bool perCharacter = false,
        CancellationToken cancellationToken = default)
    {
        string body = phrase.Trim();
        if (body.Length == 0) return "请先选择短句。";
        if (body.Length > 500) return "短句超过 500 字，请在设置中缩短。";
        string? json = await _lcu.GetStringAsync("/lol-chat/v1/conversations", cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return "未连接到客户端聊天。";
        try
        {
            JObject? conversation = JArray.Parse(json).OfType<JObject>()
                .Where(item => new[] { "championSelect", "customGame", "party" }
                    .Contains(item.Value<string>("type"), StringComparer.OrdinalIgnoreCase))
                .OrderBy(item => string.Equals(item.Value<string>("type"), "championSelect",
                    StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .FirstOrDefault();
            string? id = conversation?.Value<string>("id");
            if (string.IsNullOrWhiteSpace(id)) return "当前没有选人或房间群聊；不会发送到私人聊天。";
            string endpoint = $"/lol-chat/v1/conversations/{Uri.EscapeDataString(id)}/messages";
            if (!perCharacter)
            {
                bool sent = await _lcu.PostAsync(endpoint,
                    JsonConvert.SerializeObject(new { body, type = "chat" }), cancellationToken).ConfigureAwait(false);
                return sent ? "已发送到客户端群聊。" : "客户端拒绝了消息，请确认群聊仍可用。";
            }

            var characters = new List<string>();
            TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(body);
            while (enumerator.MoveNext())
            {
                string character = enumerator.GetTextElement();
                if (!string.IsNullOrWhiteSpace(character)) characters.Add(character);
            }
            if (characters.Count > 40) return "逐字发送最多支持 40 字，请缩短短句。";
            for (int index = 0; index < characters.Count; index++)
            {
                bool sent = await _lcu.PostAsync(endpoint,
                    JsonConvert.SerializeObject(new { body = characters[index], type = "chat" }),
                    cancellationToken).ConfigureAwait(false);
                if (!sent) return $"逐字发送在第 {index + 1} 字被客户端拒绝，已发送 {index} 字。";
                if (index < characters.Count - 1)
                    await Task.Delay(350, cancellationToken).ConfigureAwait(false);
            }
            return $"已发送到客户端群聊，共 {characters.Count} 字。";
        }
        catch (JsonException)
        {
            return "客户端聊天会话数据无法解析。";
        }
    }
}