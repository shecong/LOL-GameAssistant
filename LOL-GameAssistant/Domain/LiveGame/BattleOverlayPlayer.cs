using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Ranked;

namespace LOL_GameAssistant.Domain.LiveGame;

public sealed record BattleOverlayPlayer(GameTeamMember Member, string Name, RankedQueue? Rank,
    IReadOnlyList<bool> RecentResults, double? RecentKda, int ChampionGames, double? ChampionWinRate)
{
    public bool ProfileLoaded { get; init; }
    public bool RankLoaded { get; init; }
    public bool HistoryLoaded { get; init; }
    public bool HistoryComplete { get; init; }
    public bool NeedsRetry => !ProfileLoaded || !RankLoaded || !HistoryLoaded || !HistoryComplete;

    /// <summary>Only merge independent sources for the same player and champion context.</summary>
    public BattleOverlayPlayer Merge(BattleOverlayPlayer previous)
    {
        if (Member.Puuid != previous.Member.Puuid || Member.ChampionId != previous.Member.ChampionId) return this;
        bool keepHistory = !HistoryLoaded || (!HistoryComplete && previous.HistoryComplete);
        return this with
        {
            Name = ProfileLoaded ? Name : previous.Name,
            Rank = RankLoaded ? Rank : previous.Rank,
            RecentResults = keepHistory ? previous.RecentResults : RecentResults,
            RecentKda = keepHistory ? previous.RecentKda : RecentKda,
            ChampionGames = keepHistory ? previous.ChampionGames : ChampionGames,
            ChampionWinRate = keepHistory ? previous.ChampionWinRate : ChampionWinRate,
            ProfileLoaded = ProfileLoaded || previous.ProfileLoaded,
            RankLoaded = RankLoaded || previous.RankLoaded,
            HistoryLoaded = keepHistory ? previous.HistoryLoaded : HistoryLoaded,
            HistoryComplete = keepHistory ? previous.HistoryComplete : HistoryComplete
        };
    }
    public int ChampionWins => (int)Math.Round(ChampionGames * (ChampionWinRate ?? 0) / 100);
    public int ChampionLosses => ChampionGames - ChampionWins;
    // Only completed games in the current queue/mode count. Missing statistics are not losses.
    public static BattleOverlayPlayer Create(GameTeamMember member, string name, RankedQueue? rank,
        IEnumerable<MatchHistoryGame> history, int queueId, string mode)
    {
        var samples = history.OrderByDescending(g => g.GameCreation)
            .DistinctBy(g => g.GameId > 0 ? (object)g.GameId : g).Take(100)
            .Where(g => g.IsCompletedGame() && MatchModeComparer.IsSameMode(queueId, mode, g))
            .Select(g => g.GetParticipant(member.Puuid))
            .Where(p => p?.stats is { HasWinResult: true }).Cast<MatchParticipant>().ToArray();
        var recent = samples.Take(10).ToArray();
        var kdaSamples = recent.Where(p => p.stats!.HasKdaResult).ToArray();
        var champion = samples.Where(p => member.ChampionId > 0 && p.championId == member.ChampionId).ToArray();
        return new(member, name, rank, recent.Select(p => p.stats!.Win).ToArray(),
            kdaSamples.Length != recent.Length || recent.Length == 0 ? null : (double)recent.Sum(p => p.stats!.kills + p.stats!.assists) /
                Math.Max(1, recent.Sum(p => p.stats!.deaths)),
            champion.Length, champion.Length == 0 ? null : 100d * champion.Count(p => p.stats!.Win) / champion.Length)
        { ProfileLoaded = !string.IsNullOrWhiteSpace(name), RankLoaded = rank != null, HistoryLoaded = true, HistoryComplete = true };
    }
}

/// <summary>Public Live Client scoreboard fields. Vision score is not a count of wards placed.</summary>
public sealed record LiveScoreboardPlayer(string RiotId, string SummonerName, string Team,
    int? Kills, int? Deaths, int? Assists, int? CreepScore, double? VisionScore)
{
    public LiveGameLoadout? Loadout { get; init; }
    public string ChampionName { get; init; } = "";
    public bool IsBot { get; init; }
}

/// <summary>Only the current game's reported runes and spells; never inferred from match history.</summary>
public sealed record LiveGameAbility(int Id, string Name);
public sealed record LiveGameLoadout(LiveGameAbility? Keystone, LiveGameAbility? PrimaryRuneTree,
    LiveGameAbility? SecondaryRuneTree, LiveGameAbility? SpellOne, LiveGameAbility? SpellTwo);
