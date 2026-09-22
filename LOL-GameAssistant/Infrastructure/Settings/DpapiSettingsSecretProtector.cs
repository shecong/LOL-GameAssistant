using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.Infrastructure.Settings;

/// <summary>使用现有 Windows DPAPI 实现保护 API Key。</summary>
public sealed class DpapiSettingsSecretProtector : ISettingsSecretProtector
{
    public string Protect(string plainText) => SettingSecretProtector.Protect(plainText);
}