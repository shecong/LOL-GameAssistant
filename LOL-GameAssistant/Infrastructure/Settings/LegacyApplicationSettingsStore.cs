using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Entity;

namespace LOL_GameAssistant.Infrastructure.Settings;

/// <summary>兼容既有 settings.json 格式的设置存储适配器。</summary>
public sealed class LegacyApplicationSettingsStore : IApplicationSettingsStore
{
    private sealed class SettingsFile(string path, bool includeLegacy)
    {
        internal string Path { get; } = path;
        internal bool IncludeLegacy { get; } = includeLegacy;
        internal AssistantSettings? Current { get; set; }
    }

    private static readonly SettingsFile DefaultFile = new(SettingCache.GetCacheFilePath(), includeLegacy: true);
    private readonly SettingsFile _file;

    public LegacyApplicationSettingsStore() => _file = DefaultFile;
    internal LegacyApplicationSettingsStore(string path) => _file = new(Path.GetFullPath(path), includeLegacy: false);

    public AssistantSettings Load()
    {
        lock (_file) return Current.Copy();
    }

    public void Save(AssistantSettings settings)
    {
        lock (_file) Commit(settings);
    }

    public AssistantSettings Update(Action<AssistantSettings> update)
    {
        lock (_file)
        {
            var settings = Current.Copy();
            update(settings);
            Commit(settings);
            return Current.Copy();
        }
    }

    private AssistantSettings Current => _file.Current ??=
        LegacySettingsMapper.ToDomain(SettingCache.Load(_file.Path, _file.IncludeLegacy));

    private void Commit(AssistantSettings settings)
    {
        settings.Normalize();
        var snapshot = settings.Copy();
        SettingCache.Save(LegacySettingsMapper.ToLegacy(snapshot), _file.Path);
        _file.Current = snapshot;
    }

    public string GetStoragePath() => _file.Path;
}
