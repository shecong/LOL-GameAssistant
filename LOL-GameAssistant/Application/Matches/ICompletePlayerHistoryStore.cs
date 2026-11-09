using LOL_GameAssistant.Domain.Matches;

namespace LOL_GameAssistant.Application.Matches;

/// <summary>按真实大区与玩家隔离保存完整历史基线，不替代实时战绩摘要。</summary>
public interface ICompletePlayerHistoryStore
{
    Task<CompletePlayerHistorySnapshot?> ReadCompleteHistoryAsync(string puuid, CancellationToken token);
    Task SaveCompleteHistoryAsync(string puuid, CompletePlayerHistorySnapshot snapshot, CancellationToken token);
}

public sealed record CompletePlayerHistorySnapshot(MatchHistoryResponse Data, DateTimeOffset FullScanAt, DateTimeOffset UpdatedAt);
