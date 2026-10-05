using LOL_GameAssistant.Domain.Settings;

namespace LOL_GameAssistant.Application.Settings;

/// <summary>读取和保存助手设置的应用端口。</summary>
public interface IApplicationSettingsStore
{
    /// <summary>读取当前模块使用的持久化或内置数据。</summary>
    AssistantSettings Load();

    /// <summary>将当前模块的数据保存到持久化存储。</summary>
    void Save(AssistantSettings settings);

    /// <summary>返回当前持久化配置的文件位置。</summary>
    string GetStoragePath();
}