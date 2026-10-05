namespace LOL_GameAssistant.Application.Settings;

/// <summary>保护本地设置内敏感字段的端口。</summary>
public interface ISettingsSecretProtector
{
    /// <summary>将敏感配置转换为可保存的受保护内容。</summary>
    string Protect(string plainText);
}