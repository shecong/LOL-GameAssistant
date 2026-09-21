namespace LOL_GameAssistant.Domain.Settings;

/// <summary>云端模型的供应商与本地加密配置。</summary>
public sealed class CloudAiSettings
{
    public bool Enabled { get; set; }
    public AiProvider Provider { get; set; } = AiProvider.OpenAI;
    public string Model { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string EncryptedApiKey { get; set; } = "";
    public bool DynamicRefreshEnabled { get; set; }
    public int DynamicRefreshSeconds { get; set; } = 30;
    public bool ShowRecommendationPopup { get; set; }
    public bool RecommendationOverlayEnabled { get; set; }
    public string RecommendationOverlayPosition { get; set; } = "BottomLeft";
    public int RecommendationOverlayOffsetX { get; set; } = 24;
    public int RecommendationOverlayOffsetY { get; set; } = 48;
    public int RecommendationOverlayDurationSeconds { get; set; } = 8;
    public bool AnakinEnabled { get; set; }
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
        RecommendationOverlayPosition = string.IsNullOrWhiteSpace(RecommendationOverlayPosition) ? "BottomLeft" : RecommendationOverlayPosition;
        RecommendationOverlayOffsetX = Math.Clamp(RecommendationOverlayOffsetX, 0, 600);
        RecommendationOverlayOffsetY = Math.Clamp(RecommendationOverlayOffsetY, 0, 600);
        RecommendationOverlayDurationSeconds = Math.Clamp(RecommendationOverlayDurationSeconds, 3, 30);
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
