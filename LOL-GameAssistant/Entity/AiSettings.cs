using Newtonsoft.Json;

namespace LOL_GameAssistant.Entity;

/// <summary>
/// AI 服务商的统一配置。除密文外不保存任何 API Key 明文。
/// </summary>
public class AiSettings
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; }

    [JsonProperty("provider")]
    public AiProvider Provider { get; set; } = AiProvider.OpenAI;

    [JsonProperty("model")]
    public string Model { get; set; } = "";

    /// <summary>允许用户覆盖服务商默认地址。</summary>
    [JsonProperty("baseUrl")]
    public string BaseUrl { get; set; } = "";

    /// <summary>由 Windows DPAPI 加密后的 API Key。</summary>
    [JsonProperty("encryptedApiKey")]
    public string EncryptedApiKey { get; set; } = "";

    [JsonProperty("dynamicRefreshEnabled")]
    public bool DynamicRefreshEnabled { get; set; }

    [JsonProperty("dynamicRefreshSeconds")]
    public int DynamicRefreshSeconds { get; set; } = 30;

    [JsonProperty("showRecommendationPopup")]
    public bool ShowRecommendationPopup { get; set; }

    /// <summary>Anakin 仅作为未来 OP.GG 数据动作的可选连接，不参与当前推荐。</summary>
    [JsonProperty("anakinEnabled")]
    public bool AnakinEnabled { get; set; }

    [JsonProperty("anakinEncryptedApiKey")]
    public string AnakinEncryptedApiKey { get; set; } = "";

    public bool UsesClaudeProtocol => Provider == AiProvider.Claude;

    public string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl)) return BaseUrl.Trim().TrimEnd('/');

        return Provider switch
        {
            AiProvider.OpenAI => "https://api.openai.com/v1",
            AiProvider.Claude => "https://api.anthropic.com",
            AiProvider.DeepSeek => "https://api.deepseek.com/v1",
            AiProvider.GLM => "https://open.bigmodel.cn/api/paas/v4",
            AiProvider.Doubao => "https://ark.cn-beijing.volces.com/api/v3",
            _ => ""
        };
    }

    public string GetKeyPortalUrl() => Provider switch
    {
        AiProvider.OpenAI => "https://platform.openai.com/api-keys",
        AiProvider.Claude => "https://console.anthropic.com/",
        AiProvider.DeepSeek => "https://www.deepseek.com/platform/",
        AiProvider.GLM => "https://open.bigmodel.cn/",
        AiProvider.Doubao => "https://console.volcengine.com/ark/apiKey",
        _ => ""
    };

    public void Normalize()
    {
        Model ??= "";
        BaseUrl ??= "";
        EncryptedApiKey ??= "";
        AnakinEncryptedApiKey ??= "";
        DynamicRefreshSeconds = Math.Clamp(DynamicRefreshSeconds, 15, 600);
    }
}

public enum AiProvider
{
    OpenAI,
    Claude,
    DeepSeek,
    GLM,
    Doubao,
    CustomOpenAiCompatible
}
