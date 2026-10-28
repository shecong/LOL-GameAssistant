using LOL_GameAssistant.Application.Lobby;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Ranked;
using System.Text.Json;

namespace LOL_GameAssistant.Application.Ranked;

/// <summary>仅记录可与单场排位对应、同一段位内的赛前赛后胜点差。</summary>
internal sealed class RankedLpTracker(ILobbyService lobby, IPlayerProfileService players, IRankedStatsService ranks,
    IMatchHistoryService history, RankedLpStore store)
{
    private sealed record Baseline(long GameId, int QueueId, string Puuid, RankedQueue Rank);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Baseline? _baseline;
    private int _ending;

    internal async Task CaptureStartAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var game = await lobby.GetCurrentSessionAsync(token).ConfigureAwait(false);
            for (int attempt = 0; (game == null || game.GameId <= 0 || game.QueueId <= 0) && attempt < 10; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                if (!string.Equals(await lobby.GetGameFlowPhaseAsync(token).ConfigureAwait(false), "InProgress", StringComparison.OrdinalIgnoreCase)) return;
                game = await lobby.GetCurrentSessionAsync(token).ConfigureAwait(false);
            }
            var queue = game?.QueueId switch { 420 => RankedQueues.Solo5x5, 440 => RankedQueues.Flex5x5, _ => null };
            if (game == null || game.GameId <= 0 || queue == null) { _baseline = null; return; }
            if (_baseline?.GameId == game.GameId) return;
            _baseline = null;
            var player = await players.GetCurrentAsync(token).ConfigureAwait(false);
            if (player == null || player.IsCached || string.IsNullOrWhiteSpace(player.Puuid) ||
                !game.TeamOne.Concat(game.TeamTwo).Any(member => member.Puuid == player.Puuid)) return;
            var rank = (await ranks.GetAsync(player.Puuid, true, token).ConfigureAwait(false))?.GetQueue(queue);
            if (rank == null || rank.IsProvisional || !rank.HasWinLossRecord || !RankedDisplayRules.HasRank(rank) ||
                string.IsNullOrWhiteSpace(rank.Division)) return;
            if (!string.Equals(await lobby.GetGameFlowPhaseAsync(token).ConfigureAwait(false), "InProgress", StringComparison.OrdinalIgnoreCase)) return;
            _baseline = new(game.GameId, game.QueueId, player.Puuid, rank);
        }
        finally { _gate.Release(); }
    }

    internal async Task CaptureEndAsync(CancellationToken token = default)
    {
        if (Interlocked.Exchange(ref _ending, 1) != 0) return;
        try
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            Baseline? before;
            try { before = _baseline; }
            finally { _gate.Release(); }
            if (before == null) return;
            string queue = before.QueueId == 420 ? RankedQueues.Solo5x5 : RankedQueues.Flex5x5;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var current = await players.GetCurrentAsync(token).ConfigureAwait(false);
                    if (current == null || current.IsCached || current.Puuid != before.Puuid) return;
                    var after = (await ranks.GetAsync(before.Puuid, true, token).ConfigureAwait(false))?.GetQueue(queue);
                    var page = await history.GetPageAsync(before.Puuid, 0, 4, token).ConfigureAwait(false);
                    var game = page?.Games?.Games.FirstOrDefault(g => g.GameId == before.GameId && g.QueueId == before.QueueId);
                    if (after != null && game != null && TryCalculate(before.Rank, after, game, before.Puuid, out int delta))
                    {
                        store.Save(game.PlatformId, before.Puuid, game.GameId, delta);
                        await _gate.WaitAsync(token).ConfigureAwait(false);
                        try { if (_baseline == before) _baseline = null; }
                        finally { _gate.Release(); }
                        return;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { }
                await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
            }
        }
        finally { Interlocked.Exchange(ref _ending, 0); }
    }

    internal static bool TryCalculate(RankedQueue before, RankedQueue after, MatchHistoryGame game, string puuid, out int delta)
    {
        delta = 0;
        if (game.QueueId is not (420 or 440) || !game.IsCompletedGame() || string.IsNullOrWhiteSpace(game.PlatformId) ||
            !RankedDisplayRules.HasRank(before) || !RankedDisplayRules.HasRank(after) || string.IsNullOrWhiteSpace(before.Division) ||
            !string.Equals(before.QueueType, after.QueueType, StringComparison.OrdinalIgnoreCase) ||
            before.IsProvisional || after.IsProvisional || !before.HasWinLossRecord || !after.HasWinLossRecord ||
            !string.Equals(before.Tier, after.Tier, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(before.Division, after.Division, StringComparison.OrdinalIgnoreCase)) return false;
        var stats = game.GetParticipant(puuid)?.stats;
        if (stats is not { HasWinResult: true }) return false;
        if (after.Wins - before.Wins != (stats.Win ? 1 : 0) || after.Losses - before.Losses != (stats.Win ? 0 : 1)) return false;
        int change = after.LeaguePoints - before.LeaguePoints;
        if (change is < -100 or > 100 || (stats.Win && change < 0) || (!stats.Win && change > 0)) return false;
        delta = change; return true;
    }
}

/// <summary>按大区、玩家和对局隔离保存已核对的胜点变化，不参与可清理的网络缓存。</summary>
internal sealed class RankedLpStore
{
    internal static RankedLpStore Shared { get; } = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LOL-GameAssistant", "ranked-lp.json"));
    private readonly string _path;
    private readonly object _sync = new();
    private Dictionary<string, int>? _records;
    internal RankedLpStore(string path) => _path = path;
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
        if (string.IsNullOrWhiteSpace(platform) || string.IsNullOrWhiteSpace(puuid) || id <= 0 || value is < -100 or > 100) return;
        lock (_sync)
        {
            var copy = new Dictionary<string, int>(Records()) { [Key(platform, puuid, id)] = value };
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(copy));
            File.Move(temporary, _path, true); _records = copy;
        }
    }
}
