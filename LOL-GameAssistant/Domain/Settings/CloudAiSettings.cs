namespace LOL_GameAssistant.Domain.Settings;

/// <summary>云端模型的供应商与本地加密配置。</summary>
public sealed class CloudAiSettings
{
    /// <summary>AI 时间线建议总开关；启用后仅由已配置的 AI 服务生成建议。</summary>
    public bool RecommendationEnabled { get; set; } = true;

    public AiProvider Provider { get; set; } = AiProvider.OpenAI;
    public string Model { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string EncryptedApiKey { get; set; } = "";
    public bool DynamicRefreshEnabled { get; set; } = true;
    public int DynamicRefreshSeconds { get; set; } = 30;
    public bool ShowRecommendationPopup { get; set; }
    public bool RecommendationOverlayEnabled { get; set; }
    public string RecommendationOverlayPosition { get; set; } = "BottomLeft";
    public int RecommendationOverlayOffsetX { get; set; } = 24;
    public int RecommendationOverlayOffsetY { get; set; } = 48;
    public int RecommendationOverlayDurationSeconds { get; set; } = 8;
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

    /// <summary>
    /// 模型名不做内置候选：模型阵容会变，写死在代码里迟早过期
    /// （DeepSeek 端点上就已经不再认 deepseek-chat）。
    /// 设置页改为向服务商读取可用的模型列表。
    /// </summary>
    public void NormalizeModel() => Model = (Model ?? "").Trim();

    public void Normalize()
    {
        Model ??= "";
        BaseUrl ??= "";
        EncryptedApiKey ??= "";
        NormalizeModel();
        DynamicRefreshSeconds = Math.Clamp(DynamicRefreshSeconds, 15, 600);
        RecommendationOverlayPosition = string.IsNullOrWhiteSpace(RecommendationOverlayPosition) ? "BottomLeft" : RecommendationOverlayPosition;
        RecommendationOverlayOffsetX = Math.Clamp(RecommendationOverlayOffsetX, -600, 600);
        RecommendationOverlayOffsetY = Math.Clamp(RecommendationOverlayOffsetY, -600, 600);
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
