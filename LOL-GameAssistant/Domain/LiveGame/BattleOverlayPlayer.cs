using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Ranked;

namespace LOL_GameAssistant.Domain.LiveGame;

public sealed record BattleOverlayPlayer(GameTeamMember Member, string Name, RankedQueue? Rank,
    BattleHistoryStatistics History)
{
    public IReadOnlyList<bool> RecentResults => History.RecentResults;
    public double? RecentKda => History.RecentKda;
    public int ChampionGames => History.ChampionGames;
    public double? ChampionWinRate => History.ChampionWinRate;
    public bool ProfileLoaded { get; init; }
    public bool RankLoaded { get; init; }
    public bool HistoryLoaded { get; init; }
    public bool HistoryComplete { get; init; }
    public DateTimeOffset? HistoryAcquiredAt { get; init; }
    public bool HistoryFromCache { get; init; }
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
            History = keepHistory ? previous.History : History,
            ProfileLoaded = ProfileLoaded || previous.ProfileLoaded,
            RankLoaded = RankLoaded || previous.RankLoaded,
            HistoryLoaded = keepHistory ? previous.HistoryLoaded : HistoryLoaded,
            HistoryComplete = keepHistory ? previous.HistoryComplete : HistoryComplete,
            HistoryAcquiredAt = keepHistory ? previous.HistoryAcquiredAt : HistoryAcquiredAt,
            HistoryFromCache = keepHistory ? previous.HistoryFromCache : HistoryFromCache
        };
    }
    public int ChampionWins => History.ChampionWins;
    public int ChampionLosses => History.ChampionLosses;
    // Only completed games in the current queue/mode count. Missing statistics are not losses.
    public static BattleOverlayPlayer Create(GameTeamMember member, string name, RankedQueue? rank,
        IEnumerable<MatchHistoryGame> history, int queueId, string mode)
    {
        return new(member, name, rank, BattleHistoryStatistics.Analyze(history,
            member.Puuid, member.ChampionId, queueId, mode))
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
