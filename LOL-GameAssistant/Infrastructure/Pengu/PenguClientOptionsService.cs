using System.Text;

namespace LOL_GameAssistant.Infrastructure.Pengu;

public sealed record PenguClientOptions
{
    public bool OptimizedClient { get; init; } = true;
    public bool LowSpecMode { get; init; }
    public bool SilentMode { get; init; }
    public bool RiotClientApi { get; init; }
    public bool InsecureMode { get; init; }
}

/// <summary>Writes the native loader configuration, independently of visual settings.</summary>
public sealed class PenguClientOptionsService(Func<string> configPath)
{
    public PenguClientOptions Read()
    {
        string path = configPath();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (File.Exists(path))
            foreach (string line in File.ReadAllLines(path))
            {
                string text = line.Trim();
                if (text.StartsWith(';') || text.StartsWith('#')) continue;
                string[] parts = text.Split('=', 2);
                if (parts.Length == 2) values[parts[0].Trim()] = parts[1].Trim();
            }
        bool Get(string key, bool fallback = false) => values.GetValueOrDefault(key) switch
        {
            "true" or "1" => true, "false" or "0" => false, _ => fallback
        };
        return new()
        {
            OptimizedClient = Get("optimized_client", true), LowSpecMode = Get("super_potato"),
            SilentMode = Get("silent_mode"), RiotClientApi = Get("use_riotclient"),
            InsecureMode = Get("isecure_mode", Get("insecure_mode"))
        };
    }

    public void Save(PenguClientOptions options, bool? developerTools = null)
    {
        string path = configPath();
        var changes = new Dictionary<string, bool>
        {
            ["optimized_client"] = options.OptimizedClient, ["super_potato"] = options.LowSpecMode,
            ["silent_mode"] = options.SilentMode, ["use_riotclient"] = options.RiotClientApi,
            ["insecure_mode"] = options.InsecureMode,
            // The local native core uses this historical spelling; keep both compatible.
            ["isecure_mode"] = options.InsecureMode
        };
        if (developerTools is bool devtools) changes["use_devtools"] = devtools;
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        // The native parser ignores section names. Remove old copies so the UI and core
        // cannot disagree because a duplicate key in a later section wins.
        lines.RemoveAll(line =>
        {
            string text = line.Trim();
            if (text.StartsWith(';') || text.StartsWith('#')) return false;
            string[] parts = text.Split('=', 2);
            return parts.Length == 2 && changes.ContainsKey(parts[0].Trim());
        });
        int section = lines.FindIndex(line => line.Trim() == "[client]");
        if (section < 0) { lines.Add("[client]"); section = lines.Count - 1; }
        lines.InsertRange(section + 1, changes.Select(pair => $"{pair.Key} = {(pair.Value ? "true" : "false")}"));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, string.Join(Environment.NewLine, lines) + Environment.NewLine, new UTF8Encoding(false));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
