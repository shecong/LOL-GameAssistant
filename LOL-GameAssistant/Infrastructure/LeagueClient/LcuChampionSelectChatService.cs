using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Helper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>LCU 选人聊天实现：读取 championSelect 会话后，向其消息端点提交一条普通聊天消息。</summary>
public sealed class LcuChampionSelectChatService : IChampionSelectChatService
{
    private const int MaximumMessageLength = 1200;
    private readonly ILcuRequestSender _lcu;

    public LcuChampionSelectChatService(ILcuRequestSender lcu)
    {
        _lcu = lcu;
    }

    public async Task<ChampionSelectChatResult> SendAsync(string message, CancellationToken cancellationToken = default)
    {
        string body = message?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(body)) return ChampionSelectChatResult.Failure("聊天文案为空，未发送。");
        if (body.Length > MaximumMessageLength) body = body[..MaximumMessageLength];

        try
        {
            string? content = await _lcu.GetStringAsync("/lol-chat/v1/conversations", cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content))
                return ChampionSelectChatResult.Failure("未读取到选人聊天会话。");

            JObject? conversation = JArray.Parse(content)
                .OfType<JObject>()
                .FirstOrDefault(item => string.Equals(item.Value<string>("type"), "championSelect", StringComparison.OrdinalIgnoreCase));
            string? id = conversation?.Value<string>("id");
            if (string.IsNullOrWhiteSpace(id))
                return ChampionSelectChatResult.Failure("当前不在可发送消息的英雄选择阶段。");

            bool sent = await _lcu.PostAsync(
                $"/lol-chat/v1/conversations/{Uri.EscapeDataString(id)}/messages",
                JsonConvert.SerializeObject(new { body, type = "chat" }),
                cancellationToken).ConfigureAwait(false);
            if (!sent) return ChampionSelectChatResult.Failure("客户端拒绝了选人聊天消息。");

            RuntimeDiagnostics.Report("选人 KDA 评估", "已发送", "已通过 LCU 发送一条选人聊天汇总");
            return ChampionSelectChatResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            RuntimeDiagnostics.Report("选人 KDA 评估", "发送失败", ex.Message);
            return ChampionSelectChatResult.Failure("选人聊天会话数据不可用。");
        }
    }
}