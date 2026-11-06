using System.Text.Json;
using LOL_GameAssistant.Application.Skins;

namespace LOL_GameAssistant.Infrastructure.Skins;

public sealed class FileSkinPreferencesStore(string path) : IFileSkinPreferencesStore
{
    public FileSkinPreferences Load()
    {
        if (!File.Exists(path)) return new();
        try
        {
            var value = JsonSerializer.Deserialize<FileSkinPreferences>(File.ReadAllText(path)) ?? new();
            return new() { Favorites = new(value.Favorites ?? [], StringComparer.OrdinalIgnoreCase),
                Presets = (value.Presets ?? []).Where(p => p.Value is not null).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase) };
        }
        catch (JsonException) { return new(); }
    }
    public void Save(FileSkinPreferences preferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(preferences)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
