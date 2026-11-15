using System.Text.Json;

namespace LOL_GameAssistant.Infrastructure.Settings;

internal enum MatchDetailLayout { Paired, Dense, Matrix }

internal sealed class MatchDetailLayoutPreference(string? file = null)
{
    private readonly string _file = file ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LOL-GameAssistant", "match-detail-layout.json");
    internal MatchDetailLayout Read()
    {
        try
        {
            return File.Exists(_file) && new FileInfo(_file).Length < 1024 &&
                Enum.TryParse<MatchDetailLayout>(JsonSerializer.Deserialize<string>(File.ReadAllText(_file)), out var layout) &&
                Enum.IsDefined(layout) ? layout : MatchDetailLayout.Paired;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return MatchDetailLayout.Paired; }
    }
    internal void Save(MatchDetailLayout layout)
    {
        if (!Enum.IsDefined(layout)) throw new ArgumentOutOfRangeException(nameof(layout));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_file))!);
        string temp = _file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(layout.ToString())); File.Move(temp, _file, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
