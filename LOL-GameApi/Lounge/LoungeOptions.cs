namespace LOL_GameApi.Lounge;

public sealed class LoungeOptions
{
    public string DatabasePath { get; set; } = "data/lounge.db";
    public int RetentionDays { get; set; } = 7;
    public int MaxConnections { get; set; } = 1000;
    public int MaxConnectionsPerIp { get; set; } = 100;
    // Installation session hashes, not names supplied by the user. Reloadable from configuration.
    public string[] MutedSessions { get; set; } = [];
    public string[] BannedSessions { get; set; } = [];
    public string AdminKey { get; set; } = "";
}
