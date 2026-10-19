using LOL_GameAssistant.Domain.MatchAnalysis;

namespace LOL_GameAssistant.Application.Matches;

public sealed record ChampionArchiveEntry(int ChampionId, string Name, bool? Owned, long? MasteryPoints,
    int? MasteryLevel, bool Training, ChampionGrowth? Recent);
public sealed record PlayerGrowthSnapshot(string Puuid, GrowthReport Report, IReadOnlyList<ChampionArchiveEntry> Champions,
    IReadOnlyList<string> Modes, DateTimeOffset AcquiredAt, bool Cached, string Message)
{
    public IReadOnlyDictionary<long, string> ReviewNotes { get; init; } = new Dictionary<long, string>();
}
public interface IPlayerGrowthService
{
    Task<PlayerGrowthSnapshot> LoadAsync(string puuid, string mode, CancellationToken token = default, bool refresh = false);
    Task SetTrainingAsync(string puuid, int championId, bool selected, CancellationToken token = default);
    Task SetReviewNoteAsync(string puuid, long gameId, string note, CancellationToken token = default);
}
