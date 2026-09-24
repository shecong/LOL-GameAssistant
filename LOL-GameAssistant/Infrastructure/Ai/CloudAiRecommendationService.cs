using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LOL_GameAssistant.Infrastructure.Ai;

/// <summary>
/// 云端模型的最小适配层。所有服务商输出均收敛为可读的中文建议，密钥不会写入请求日志。
/// 失败时保留服务端返回的原因：只报“服务返回 400”时，用户无法区分模型名写错、
/// Key 无效还是余额不足，只能靠猜。
/// </summary>
public sealed class CloudAiRecommendationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;

    public CloudAiRecommendationService(HttpClient http) => _http = http;

    public async Task<AiRecommendationResult> GetRecommendationAsync(
        CloudAiSettings settings,
        AiGameContext context,
        CancellationToken cancellationToken = default)
    {
        string contextSummary = BuildContextSummary(context);

        string key;
        try
        {
            key = ReadApiKey(settings);
            EnsureConfigured(settings, key);
        }
        catch (InvalidOperationException ex)
        {
            return AiRecommendationResult.Unavailable(contextSummary, context, ex.Message);
        }

        try
        {
            string content = await CallAsync(
                settings,
                key,
                SystemPrompt,
                BuildUserPrompt(context),
                cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// 用一次最小请求验证服务商、模型与密钥是否可用，不发送任何对局数据。
    /// 失败时抛出带原因的中文说明，供设置页直接显示。
    /// </summary>
    public async Task<string> TestConnectionAsync(
        CloudAiSettings settings,
        CancellationToken cancellationToken = default)
    {
        string key = ReadApiKey(settings);
        EnsureConfigured(settings, key);

        string reply;
        try
        {
            reply = await CallAsync(
                settings,
                key,
                "你是连接测试助手。",
                "只回复两个字：正常",
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(ToFriendlyError(ex), ex);
        }

        return string.IsNullOrWhiteSpace(reply) ? "（无内容）" : reply.Trim();
    }

    /// <summary>
    /// 读取服务商当前可用的模型列表。模型阵容会变，写死在代码里迟早过期
    /// （DeepSeek 就已经不再认 deepseek-chat），所以以服务端返回为准。
    /// </summary>
    public async Task<IReadOnlyList<string>> ListModelsAsync(
        CloudAiSettings settings,
        CancellationToken cancellationToken = default)
    {
        string key = ReadApiKey(settings);
        EnsureConfigured(settings, key);

        string url = settings.GetBaseUrl() + (settings.UsesClaudeProtocol ? "/v1/models" : "/models");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (settings.UsesClaudeProtocol)
        {
            request.Headers.Add("x-api-key", key);
            request.Headers.Add("anthropic-version", "2023-06-01");
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new AiServiceException(response.StatusCode, body);

        using JsonDocument document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("服务没有返回模型列表。");

        var models = new List<string>();
        foreach (JsonElement item in data.EnumerateArray())
        {
            if (item.TryGetProperty("id", out JsonElement id) && !string.IsNullOrWhiteSpace(id.GetString()))
                models.Add(id.GetString()!);
        }
        return models;
    }

    private static string ReadApiKey(CloudAiSettings settings)
    {
        try
        {
            return SettingSecretProtector.Unprotect(settings.EncryptedApiKey);
        }
        catch
        {
            throw new InvalidOperationException("无法读取已保存的 API Key，请重新保存。");
        }
    }

    private static void EnsureConfigured(CloudAiSettings settings, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("未配置 API Key。");
        if (string.IsNullOrWhiteSpace(settings.Model))
            throw new InvalidOperationException("未填写模型名称。");
        if (string.IsNullOrWhiteSpace(settings.GetBaseUrl()))
            throw new InvalidOperationException("未配置有效的服务地址。");
    }

    private Task<string> CallAsync(
        CloudAiSettings settings,
        string key,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken) =>
        settings.UsesClaudeProtocol
            ? CallClaudeAsync(settings, key, systemPrompt, userPrompt, cancellationToken)
            : CallOpenAiCompatibleAsync(settings, key, systemPrompt, userPrompt, cancellationToken);

    private async Task<string> CallOpenAiCompatibleAsync(
        CloudAiSettings settings,
        string key,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        string url = settings.GetBaseUrl() + "/chat/completions";
        var payload = new
        {
            model = settings.Model,
            temperature = 0.2,
            max_tokens = 900,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new AiServiceException(response.StatusCode, body);

        using JsonDocument document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
               ?? throw new InvalidOperationException("服务没有返回建议内容。");
    }

    private async Task<string> CallClaudeAsync(
        CloudAiSettings settings,
        string key,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        string url = settings.GetBaseUrl() + "/v1/messages";
        var payload = new
        {
            model = settings.Model,
            max_tokens = 900,
            temperature = 0.2,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = userPrompt } }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("x-api-key", key);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new AiServiceException(response.StatusCode, body);

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
        AiServiceException => ex.Message,
        HttpRequestException => "网络或接口地址连接失败。",
        TaskCanceledException => "请求超时，请稍后重试。",
        JsonException => "服务返回格式无法识别，请检查服务商与模型配置。",
        _ => ex.Message
    };

    /// <summary>服务端拒绝时的响应：原因翻译与脱敏由 <see cref="AiServiceErrorMessage"/> 负责。</summary>
    private sealed class AiServiceException : Exception
    {
        public AiServiceException(HttpStatusCode status, string body)
            : base(AiServiceErrorMessage.Describe((int)status, body))
        {
        }
    }

    private const string SystemPrompt = """
        你是《英雄联盟》中文训练助手。仅基于用户提供的、玩家正常可见的信息做建议；不得推测敌方位置、冷却、未展示装备或任何隐藏信息。
        用自然中文写 1–3 条简洁的时间线建议，每条按“【类别】标题”“建议：具体思路与适用条件”“依据：给定的可见时间、金币、装备或阵容”分行呈现。只输出用户能直接阅读的文字，不输出 JSON、代码块或 JSON 字段名。
        不操作客户端，不要求玩家立刻执行某一动作；使用“可考虑”“适合于”等表达选择条件。不要复述敏感数据。
        """;
}
