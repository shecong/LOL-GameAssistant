using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Entity;

namespace LOL_GameAssistant.Infrastructure.Settings;

/// <summary>兼容既有 settings.json 格式的设置存储适配器。</summary>
public sealed class LegacyApplicationSettingsStore : IApplicationSettingsStore
{
    public AssistantSettings Load() => LegacySettingsMapper.ToDomain(SettingCache.Load());

    public void Save(AssistantSettings settings) => SettingCache.Save(LegacySettingsMapper.ToLegacy(settings));

    public string GetStoragePath() => SettingCache.GetCacheFilePath();
}
