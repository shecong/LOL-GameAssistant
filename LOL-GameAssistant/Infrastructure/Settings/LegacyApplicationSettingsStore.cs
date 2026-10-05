using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Entity;

namespace LOL_GameAssistant.Infrastructure.Settings;

/// <summary>兼容既有 settings.json 格式的设置存储适配器。</summary>
public sealed class LegacyApplicationSettingsStore : IApplicationSettingsStore
{
    /// <summary>读取当前模块使用的持久化或内置数据。</summary>
    public AssistantSettings Load() => LegacySettingsMapper.ToDomain(SettingCache.Load());

    /// <summary>将当前模块的数据保存到持久化存储。</summary>
    public void Save(AssistantSettings settings) => SettingCache.Save(LegacySettingsMapper.ToLegacy(settings));

    /// <summary>返回当前持久化配置的文件位置。</summary>
    public string GetStoragePath() => SettingCache.GetCacheFilePath();
}