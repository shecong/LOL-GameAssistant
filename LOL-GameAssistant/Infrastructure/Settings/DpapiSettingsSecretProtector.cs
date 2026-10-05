using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.Infrastructure.Settings;

/// <summary>使用现有 Windows DPAPI 实现保护 API Key。</summary>
public sealed class DpapiSettingsSecretProtector : ISettingsSecretProtector
{
    /// <summary>将敏感配置转换为可保存的受保护内容。</summary>
    public string Protect(string plainText) => SettingSecretProtector.Protect(plainText);
}