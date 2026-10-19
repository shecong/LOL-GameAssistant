namespace LOL_GameAssistant.Application.Matches;

/// <summary>战绩读取的统一预算；排队、网络和界面等待分别限时。</summary>
public static class MatchDataPolicy
{
    public const int SummaryLimit = 500;
    public const int SummaryConcurrency = 6;
    public const int DetailConcurrency = 6;
    public const int VisibleRecentGames = 10;
    public static readonly TimeSpan SummaryTimeout = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan SmallRequestTimeout = TimeSpan.FromSeconds(12);
    public static readonly TimeSpan DetailTimeout = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan LoadTimeout = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Freshness = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan TransportTimeout = TimeSpan.FromMinutes(2);
}

/// <summary>不包含认证信息的客户端和账号隔离标识。</summary>
public static class PlayerHistorySession
{
    private static readonly object Sync = new();
    private static string _client = "initial", _account = "unknown";
    public static string Key { get { lock (Sync) return _client + ":" + _account; } }
    public static void SetClient(string value) { lock (Sync) { if (_client != value) { _client = value; _account = "unknown"; } } }
    public static void SetAccount(string value) { if (!string.IsNullOrWhiteSpace(value)) lock (Sync) _account = value; }
}
