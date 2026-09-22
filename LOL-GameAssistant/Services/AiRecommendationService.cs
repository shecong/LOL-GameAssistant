using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LOL_GameAssistant.Infrastructure.Ai;

/// <summary>
/// 云端模型的最小适配层。所有服务商输出均收敛为纯文本建议，密钥不会写入请求日志。
/// </summary>
public static class CloudAiRecommendationService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task<AiRecommendationResult> GetRecommendationAsync(
        CloudAiSettings settings,
        AiGameContext context,
        CancellationToken cancellationToken = default)
    {
        string contextSummary = BuildContextSummary(context);

        string key;
        try
        {
            key = SettingSecretProtector.Unprotect(settings.EncryptedApiKey);
        }
        catch
        {
            return AiRecommendationResult.Unavailable(
                contextSummary, context, "无法读取已保存的 API Key，请重新保存。");
        }
        if (string.IsNullOrWhiteSpace(key))
            return AiRecommendationResult.Unavailable(contextSummary, context, "未配置 API Key。");
        if (string.IsNullOrWhiteSpace(settings.Model))
            return AiRecommendationResult.Unavailable(contextSummary, context, "未填写模型名称。");
        if (string.IsNullOrWhiteSpace(settings.GetBaseUrl()))
            return AiRecommendationResult.Unavailable(contextSummary, context, "未配置有效的服务地址。");

        try
        {
            string content = settings.UsesClaudeProtocol
                ? await CallClaudeAsync(settings, key, context, cancellationToken).ConfigureAwait(false)
                : await CallOpenAiCompatibleAsync(settings, key, context, cancellationToken).ConfigureAwait(false);
            return new AiRecommendationResult(true, contextSummary, content.Trim(), context, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 不返回原始请求、响应或密钥；这些信息可能包含用户敏感配置。
            return AiRecommendationResult.Unavailable(contextSummary, context, ToFriendlyError(ex));
        }
    }

    private static async Task<string> CallOpenAiCompatibleAsync(CloudAiSettings settings, string key, AiGameContext context, CancellationToken cancellationToken)
    {
        string url = settings.GetBaseUrl() + "/chat/completions";
        var payload = new
        {
            model = settings.Model,
            temperature = 0.2,
            max_tokens = 900,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = BuildUserPrompt(context) }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"服务返回 {(int)response.StatusCode}");

        using JsonDocument document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
               ?? throw new InvalidOperationException("服务没有返回建议内容。");
    }

    private static async Task<string> CallClaudeAsync(CloudAiSettings settings, string key, AiGameContext context, CancellationToken cancellationToken)
    {
        string url = settings.GetBaseUrl() + "/v1/messages";
        var payload = new
        {
            model = settings.Model,
            max_tokens = 900,
            temperature = 0.2,
            system = SystemPrompt,
            messages = new[] { new { role = "user", content = BuildUserPrompt(context) } }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("x-api-key", key);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"服务返回 {(int)response.StatusCode}");

        using JsonDocument document = JsonDocument.Parse(body);
        foreach (JsonElement block in document.RootElement.GetProperty("content").EnumerateArray())
        {
            if (block.TryGetProperty("type", out var type) && type.GetString() == "text" &&
                block.TryGetProperty("text", out var text) && !string.IsNullOrWhiteSpace(text.GetString()))
            {
                return text.GetString()!;
            }
        }
        throw new InvalidOperationException("服务没有返回建议内容。");
    }

    private static string BuildUserPrompt(AiGameContext context)
    {
        string scope = context.IsAram
            ? "这是大乱斗：围绕团战节奏、敌方伤害构成与可见装备给出可选出装方向。"
            : "这是峡谷：给出对线、核心装备、可选防御/反制装备、符文与召唤师技能校验。";
        return $"{scope}\n\n{context.ToPromptText()}";
    }

    private static string BuildContextSummary(AiGameContext context)
    {
        var lines = new List<string>
        {
            "发送给 AI 的当前对局数据",
            $"模式：{context.Mode}",
            $"英雄：{context.MyChampion}（{context.MyRole}）"
        };

        if (context.CurrentItems.Count > 0)
            lines.Add("已购装备：" + string.Join("、", context.CurrentItems));
        if (context.CurrentGold > 0)
            lines.Add($"当前可用金币：{context.CurrentGold}");
        if (context.GameTimeSeconds > 0)
            lines.Add($"游戏时间：{context.GameTimeText}");
        if (context.EnemyChampions.Count > 0)
            lines.Add("已知敌方阵容：" + string.Join("、", context.EnemyChampions));
        lines.Add("对线知识：" + context.LaneKnowledge);
        return string.Join(Environment.NewLine, lines);
    }

    private static string ToFriendlyError(Exception ex) => ex switch
    {
        HttpRequestException => "网络或接口地址连接失败。",
        TaskCanceledException => "请求超时，请稍后重试。",
        JsonException => "服务返回格式无法识别，请检查服务商与模型配置。",
        _ => ex.Message
    };

    private const string SystemPrompt = """
        你是《英雄联盟》中文训练助手。仅基于用户提供的、玩家正常可见的信息做建议；不得推测敌方位置、冷却、未展示装备或任何隐藏信息。
        只输出 JSON，不要 Markdown 或额外说明，格式为：
        {"recommendations":[{"category":"装备/对线/团战/符文","priority":"info/attention/important","title":"不超过 24 字","body":"不超过 120 字，说明可选思路与适用条件","evidence":"引用给定的可见时间、金币、装备或阵容"}]}
        最多 3 条，不操作客户端、不要求玩家立刻执行某一动作；使用“可考虑”“适合于”等表达选择条件。不要复述敏感数据。
        """;
}
