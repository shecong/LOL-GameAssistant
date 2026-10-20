using System.Text.Json;
using LOL_GameAssistant.Domain.Matches;

namespace LOL_GameAssistant.Application.Matches;

public enum HistoryDataState { Ready, Cached, Unavailable }

/// <summary>不可变的摘要快照。读模型按需复制，窗口不能修改其他窗口使用的样本。</summary>
public sealed record PlayerHistorySnapshot
{
    private readonly byte[]? _payload;
    public string Puuid { get; init; }
    public DateTimeOffset AcquiredAt { get; init; }
    public HistoryDataState State { get; init; }
    public string? Error { get; init; }
    public MatchHistoryResponse? Data => _payload == null ? null : JsonSerializer.Deserialize<MatchHistoryResponse>(_payload);
    public int RequestedCount => MatchDataPolicy.SummaryLimit;
    public int ReturnedCount { get; }
    public string DataWarning { get; }
    public bool Available { get; }
    public bool IsStale => State == HistoryDataState.Cached;
    public bool IsCompleteHistory => false;
    public DateTimeOffset? OldestGame { get; }
    public DateTimeOffset? LatestGame { get; }
    public string ScopeText => $"{(IsStale ? "缓存" : "已获取")} {ReturnedCount} 场 · {AcquiredAt.ToLocalTime():MM-dd HH:mm} · 有限样本" +
        (DataWarning.Length > 0 ? $" · {DataWarning}" : "");
    public PlayerHistorySnapshot(string puuid, MatchHistoryResponse? data, DateTimeOffset acquiredAt,
        HistoryDataState state, string? error = null)
    {
        Puuid = puuid; AcquiredAt = acquiredAt; State = state; Error = error;
        Available = data?.Games != null;
        ReturnedCount = data?.Games?.Games.Count ?? 0;
        DataWarning = data?.Games?.DataWarning ?? "";
        _payload = data == null ? null : JsonSerializer.SerializeToUtf8Bytes(data);
        var dates = data?.Games?.Games.Select(game => game.GameCreation)
            .Where(value => value > 0 && value <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds()).ToArray() ?? [];
        OldestGame = dates.Length == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(dates.Min());
        LatestGame = dates.Length == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(dates.Max());
    }
}

public interface IPlayerHistorySnapshotSource
{
    Task<PlayerHistorySnapshot> ReadSnapshotAsync(string puuid, CancellationToken cancellationToken);
}
