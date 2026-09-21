using LOL_GameAssistant.Domain.Settings;

namespace LOL_GameAssistant.Application.Settings;

/// <summary>读取和保存助手设置的应用端口。</summary>
public interface IApplicationSettingsStore
{
    AssistantSettings Load();

    void Save(AssistantSettings settings);

    string GetStoragePath();
}
