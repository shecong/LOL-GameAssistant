using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;

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
        string localValidation = BuildLocalValidation(context);
        if (!settings.Enabled)
            return AiRecommendationResult.LocalOnly(localValidation, "云端 AI 未启用。可在设置中启用并配置模型。", context);

        string key = SettingSecretProtector.Unprotect(settings.EncryptedApiKey);
        if (string.IsNullOrWhiteSpace(key))
            return AiRecommendationResult.LocalOnly(localValidation, "未配置 API Key，当前仅展示本地规则与对线知识。", context);
        if (string.IsNullOrWhiteSpace(settings.Model))
            return AiRecommendationResult.LocalOnly(localValidation, "未填写模型名称，当前仅展示本地规则与对线知识。", context);
        if (string.IsNullOrWhiteSpace(settings.GetBaseUrl()))
            return AiRecommendationResult.LocalOnly(localValidation, "未配置有效的服务地址，当前仅展示本地规则与对线知识。", context);

        try
        {
            string content = settings.UsesClaudeProtocol
                ? await CallClaudeAsync(settings, key, context, cancellationToken).ConfigureAwait(false)
                : await CallOpenAiCompatibleAsync(settings, key, context, cancellationToken).ConfigureAwait(false);
            return new AiRecommendationResult(true, localValidation, content.Trim(), context, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 不返回原始请求、响应或密钥；这些信息可能包含用户敏感配置。
            return AiRecommendationResult.LocalOnly(localValidation, "云端 AI 暂时不可用：" + ToFriendlyError(ex), context);
        }
    }

    private static async Task<string> CallOpenAiCompatibleAsync(CloudAiSettings settings, string key, AiGameContext context, CancellationToken cancellationToken)
    {
        string url = settings.GetBaseUrl() + "/chat/completions";
        var payload = new
        {
            model = settings.Model,
            temperature = 0.2,
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

    private static string BuildLocalValidation(AiGameContext context)
    {
        var lines = new List<string>
        {
            "本地校验",
            $"模式：{context.Mode}",
            $"英雄：{context.MyChampion}（{context.MyRole}）"
        };

        if (context.CurrentItems.Count > 0)
            lines.Add("已购装备：" + string.Join("、", context.CurrentItems));
        if (context.CurrentGold > 0)
            lines.Add($"当前可用金币：{context.CurrentGold}");
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
        输出简洁中文，固定包含：
        1. 当前判断；
        2. 核心装备方向（2 至 3 件）；
        3. 至少两种可选反制/替换思路及适用条件；
        4. 符文和召唤师技能校验；
        5. 1 至 3 条对线/团战知识点。
        不操作客户端、不要求玩家立刻执行某一动作；用“可考虑”“适合于”等表达选择条件。不要输出 JSON、不要复述敏感数据。
        """;
}
