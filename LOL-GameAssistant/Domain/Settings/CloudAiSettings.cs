namespace LOL_GameAssistant.Domain.Settings;

/// <summary>云端模型的供应商与本地加密配置。</summary>
public sealed class CloudAiSettings
{
    /// <summary>时间线总开关；本机状态提醒与已配置的云端战术分析均受此开关控制。</summary>
    public bool RecommendationEnabled { get; set; } = true;

    public AiProvider Provider { get; set; } = AiProvider.OpenAI;
    public string Model { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string EncryptedApiKey { get; set; } = "";
    public bool DynamicRefreshEnabled { get; set; } = true;
    public int DynamicRefreshSeconds { get; set; } = 90;
    public int MinimumRequestSeconds { get; set; } = 30;
    public int MatchRequestBudget { get; set; } = 30;
    /// <summary>仅保留旧配置的读写兼容；当前行动计划始终经过结构化事实校验。</summary>
    public bool StructuredOutputEnabled { get; set; } = true;
    public CloudAiSettings Copy() => (CloudAiSettings)MemberwiseClone();
    public bool ShowRecommendationPopup { get; set; }
    public bool RecommendationOverlayEnabled { get; set; }
    public string RecommendationOverlayPosition { get; set; } = "BottomLeft";
    public int RecommendationOverlayOffsetX { get; set; } = 24;
    public int RecommendationOverlayOffsetY { get; set; } = 48;
    public int RecommendationOverlayDurationSeconds { get; set; } = 8;
    /// <summary>判断当前服务配置是否需要使用 Claude 消息协议。</summary>
    public bool UsesClaudeProtocol => Provider == AiProvider.Claude;

    /// <summary>取得当前 AI 服务提供商的请求地址。</summary>
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

    /// <summary>密钥只能发送到 HTTPS 服务，或明确指定的本机开发服务。</summary>
    public bool TryGetSafeBaseUri(out Uri? uri, out string error)
    {
        string address = GetBaseUrl();
        if (!Uri.TryCreate(address, UriKind.Absolute, out uri) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            error = "AI 服务地址必须是有效的绝对 URL，且不能包含账号、查询参数或片段。";
            return false;
        }

        bool local = uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host == "127.0.0.1";
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && local))
        {
            error = "AI 服务地址必须使用 HTTPS；仅 localhost 或 127.0.0.1 可使用 HTTP。";
            return false;
        }
        error = "";
        return true;
    }

    /// <summary>取得当前 AI 服务提供商的密钥管理地址。</summary>
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

    /// <summary>将配置或文本输入规范化为后续处理使用的形式。</summary>
    public void Normalize()
    {
        Model ??= "";
        BaseUrl ??= "";
        EncryptedApiKey ??= "";
        NormalizeModel();
        DynamicRefreshSeconds = Math.Clamp(DynamicRefreshSeconds, 15, 600);
        MinimumRequestSeconds = Math.Clamp(MinimumRequestSeconds, 8, 300);
        MatchRequestBudget = Math.Clamp(MatchRequestBudget, 1, 200);
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
