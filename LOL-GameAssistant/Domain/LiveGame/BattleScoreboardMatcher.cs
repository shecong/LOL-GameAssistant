namespace LOL_GameAssistant.Domain.LiveGame;

/// <summary>Match live data only to a unique player on the expected team.</summary>
public static class BattleScoreboardMatcher
{
    public static LiveScoreboardPlayer? Find(IReadOnlyList<LiveScoreboardPlayer>? scores,
        string name, string fallback, string team)
    {
        string identity = name.Contains('#') ? name : fallback.Contains('#') ? fallback : name;
        bool fullIdentity = identity.Contains('#');
        var exact = scores?.Where(player => player.Team == team && !string.IsNullOrWhiteSpace(player.RiotId) &&
            string.Equals(player.RiotId, identity, StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
        if (exact.Length == 1) return exact[0];
        if (exact.Length > 1) return null;
        var legacy = scores?.Where(player => player.Team == team && !string.IsNullOrWhiteSpace(fallback) &&
            string.Equals(player.SummonerName, fallback, StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
        if (legacy.Length != 1) return null;
        return fullIdentity && legacy[0].RiotId.Contains('#') &&
            !string.Equals(legacy[0].RiotId, identity, StringComparison.OrdinalIgnoreCase) ? null : legacy[0];
    }
}
