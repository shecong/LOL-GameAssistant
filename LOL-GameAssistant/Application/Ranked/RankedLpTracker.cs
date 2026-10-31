using LOL_GameAssistant.Application.Lobby;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Ranked;
using System.Text.Json;

namespace LOL_GameAssistant.Application.Ranked;

internal sealed record RankedLpBaseline(long GameId, int QueueId, string Puuid, RankedQueue Rank, DateTimeOffset CapturedAt);

/// <summary>按单场胜负计数关联快照，支持跨小段并在客户端重启后继续核对。</summary>
internal sealed class RankedLpTracker(ILobbyService lobby, IPlayerProfileService players, IRankedStatsService ranks,
    IMatchHistoryService history, RankedLpStore store, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private RankedLpBaseline? _baseline = store.LoadPending();
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private int _ending;

    internal async Task CaptureStartAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var game = await lobby.GetCurrentSessionAsync(token).ConfigureAwait(false);
            for (int attempt = 0; (game == null || game.GameId <= 0 || game.QueueId <= 0) && attempt < 10; attempt++)
            {
                await _delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                if (!ActivePhase(await lobby.GetGameFlowPhaseAsync(token).ConfigureAwait(false))) return;
                game = await lobby.GetCurrentSessionAsync(token).ConfigureAwait(false);
            }
            var queue = game?.QueueId switch { 420 => RankedQueues.Solo5x5, 440 => RankedQueues.Flex5x5, _ => null };
            if (game == null || game.GameId <= 0 || queue == null) return;
            if (_baseline?.GameId == game.GameId) return;
            var player = await players.GetCurrentAsync(token).ConfigureAwait(false);
            if (player == null || player.IsCached || string.IsNullOrWhiteSpace(player.Puuid)) return;
            var overview = await ranks.GetAsync(player.Puuid, true, token).ConfigureAwait(false);
            var rank = overview is { IsCached: false } ? overview.GetQueue(queue) : null;
            if (rank == null || rank.IsProvisional || !rank.HasWinLossRecord || !RankedDisplayRules.HasRank(rank) ||
                string.IsNullOrWhiteSpace(rank.Division)) return;
            if (!ActivePhase(await lobby.GetGameFlowPhaseAsync(token).ConfigureAwait(false))) return;
            if ((await lobby.GetCurrentSessionAsync(token).ConfigureAwait(false))?.GameId != game.GameId) return;
            _baseline = new(game.GameId, game.QueueId, player.Puuid, rank, DateTimeOffset.UtcNow);
            store.SavePending(_baseline);
            Report("已采集赛前快照，等待该局结算。");
        }
        finally { _gate.Release(); }
    }

    internal async Task CaptureEndAsync(CancellationToken token = default)
    {
        if (Interlocked.Exchange(ref _ending, 1) != 0) return;
        try
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            RankedLpBaseline? before;
            try { before = _baseline; }
            finally { _gate.Release(); }
            if (before == null || DateTimeOffset.UtcNow - before.CapturedAt > TimeSpan.FromHours(6)) return;
            string queue = before.QueueId == 420 ? RankedQueues.Solo5x5 : RankedQueues.Flex5x5;
            RankedQueue? candidate = null; int confirmations = 0;
            for (int attempt = 0; attempt < 48; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var current = await players.GetCurrentAsync(token).ConfigureAwait(false);
                    if (current == null || current.IsCached || current.Puuid != before.Puuid) return;
                    var overview = await ranks.GetAsync(before.Puuid, true, token).ConfigureAwait(false);
                    var after = overview is { IsCached: false } ? overview.GetQueue(queue) : null;
                    var page = await history.GetPageAsync(before.Puuid, 0, 19, token).ConfigureAwait(false);
                    var game = page?.Games?.Games.FirstOrDefault(g => g.GameId == before.GameId && g.QueueId == before.QueueId);
                    if (after != null && game != null && TryCalculate(before.Rank, after, game, before.Puuid, out int delta))
                    {
                        confirmations = candidate == after ? confirmations + 1 : 1;
                        candidate = after;
                        // Ranked counters, LP and history may arrive in separate updates.
                        if (confirmations < 3) { await _delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false); continue; }
                        store.Save(game.PlatformId, before.Puuid, game.GameId, delta);
                        await _gate.WaitAsync(token).ConfigureAwait(false);
                        try { if (_baseline == before) { _baseline = null; store.ClearPending(before.GameId); } }
                        finally { _gate.Release(); }
                        Report($"已核对单场排位变化：{delta:+0;-0;0} LP。"); return;
                    }
                    candidate = null; confirmations = 0;
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { candidate = null; confirmations = 0; }
                await _delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
            }
            Report("结算数据尚未完成核对，已保留赛前快照，返回大厅后可继续补录。");
        }
        finally { Interlocked.Exchange(ref _ending, 0); }
    }

    private static bool ActivePhase(string? phase) => phase?.ToLowerInvariant() is "gamestart" or "inprogress";
    private static void Report(string message) => LOL_GameAssistant.Helper.RuntimeDiagnostics.Report("排位胜点", "记录", message);

    internal static bool TryCalculate(RankedQueue before, RankedQueue after, MatchHistoryGame game, string puuid, out int delta)
    {
        delta = 0;
        if (game.QueueId is not (420 or 440) || !game.IsCompletedGame() || string.IsNullOrWhiteSpace(game.PlatformId) ||
            !RankedDisplayRules.HasRank(before) || !RankedDisplayRules.HasRank(after) ||
            !string.Equals(before.QueueType, after.QueueType, StringComparison.OrdinalIgnoreCase) ||
            before.QueueType != (game.QueueId == 420 ? RankedQueues.Solo5x5 : RankedQueues.Flex5x5) ||
            before.IsProvisional || after.IsProvisional || !before.HasWinLossRecord || !after.HasWinLossRecord ||
            before.Wins < 0 || before.Losses < 0 || after.Wins < 0 || after.Losses < 0) return false;
        var stats = game.GetParticipant(puuid)?.stats;
        if (stats is not { HasWinResult: true }) return false;
        if (after.Wins - before.Wins != (stats.Win ? 1 : 0) || after.Losses - before.Losses != (stats.Win ? 0 : 1)) return false;
        if (!TryLadderPoints(before, out int oldPoints) || !TryLadderPoints(after, out int newPoints)) return false;
        int change = newPoints - oldPoints;
        if (change is < -400 or > 400 || (stats.Win && change < 0) || (!stats.Win && change > 0)) return false;
        delta = change; return true;
    }

    internal static bool TryLadderPoints(RankedQueue rank, out int points)
    {
        points = 0;
        string[] tiers = ["IRON", "BRONZE", "SILVER", "GOLD", "PLATINUM", "EMERALD", "DIAMOND"];
        string tier = rank.Tier.ToUpperInvariant();
        if (rank.LeaguePoints < 0) return false;
        if (tier is "MASTER" or "GRANDMASTER" or "CHALLENGER")
        { if (rank.LeaguePoints > 10000) return false; points = 2800 + rank.LeaguePoints; return true; }
        int index = Array.IndexOf(tiers, tier);
        int division = rank.Division.ToUpperInvariant() switch { "IV" => 0, "III" => 1, "II" => 2, "I" => 3, _ => -1 };
        if (index < 0 || division < 0 || rank.LeaguePoints >= 100) return false;
        points = index * 400 + division * 100 + rank.LeaguePoints; return true;
    }
}

/// <summary>按大区、玩家和对局隔离保存已核对的胜点变化，不参与可清理的网络缓存。</summary>
internal sealed class RankedLpStore
{
    internal static RankedLpStore Shared { get; } = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LOL-GameAssistant", "ranked-lp.json"));
    private readonly string _path;
    private readonly object _sync = new();
    private Dictionary<string, int>? _records;
    internal event Action? Changed;
    internal RankedLpStore(string path) => _path = path;
    internal RankedLpBaseline? LoadPending()
    {
        lock (_sync)
        {
            try
            {
                if (!File.Exists(_path + ".pending")) return null;
                var pending = JsonSerializer.Deserialize<RankedLpBaseline>(File.ReadAllText(_path + ".pending"));
                return pending is { GameId: > 0, QueueId: 420 or 440, Rank: not null } && !string.IsNullOrWhiteSpace(pending.Puuid) &&
                    pending.CapturedAt <= DateTimeOffset.UtcNow && DateTimeOffset.UtcNow - pending.CapturedAt < TimeSpan.FromHours(6) ? pending : null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
        }
    }
    internal void SavePending(RankedLpBaseline pending)
    {
        lock (_sync) WriteAtomic(_path + ".pending", JsonSerializer.Serialize(pending));
    }
    internal void ClearPending(long gameId)
    {
        lock (_sync) if (LoadPending()?.GameId == gameId) WriteAtomic(_path + ".pending", "null");
    }
    private static void WriteAtomic(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path + ".tmp", json); File.Move(path + ".tmp", path, true);
    }
    private static string Key(string platform, string puuid, long id) => $"{platform.ToUpperInvariant()}|{puuid}|{id}";
    private Dictionary<string, int> Records()
    {
        if (_records != null) return _records;
        _records = File.Exists(_path) ? JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(_path)) ?? [] : [];
        return _records;
    }
    internal int? Get(string platform, string puuid, long id)
    {
        if (string.IsNullOrWhiteSpace(platform) || string.IsNullOrWhiteSpace(puuid) || id <= 0) return null;
        lock (_sync)
        {
            try { return Records().TryGetValue(Key(platform, puuid, id), out int value) ? value : null; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
        }
    }
    internal void Save(string platform, string puuid, long id, int value)
    {
        if (string.IsNullOrWhiteSpace(platform) || string.IsNullOrWhiteSpace(puuid) || id <= 0 || value is < -400 or > 400) return;
        lock (_sync)
        {
            var copy = new Dictionary<string, int>(Records()) { [Key(platform, puuid, id)] = value };
            WriteAtomic(_path, JsonSerializer.Serialize(copy)); _records = copy;
        }
        Changed?.Invoke();
    }
}
