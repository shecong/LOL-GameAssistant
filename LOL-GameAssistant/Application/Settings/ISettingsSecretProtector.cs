namespace LOL_GameAssistant.Application.Settings;

/// <summary>保护本地设置内敏感字段的端口。</summary>
public interface ISettingsSecretProtector
{
    string Protect(string plainText);
}