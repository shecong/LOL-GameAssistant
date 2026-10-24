using LOL.Lounge;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace LOL_GameApi.Lounge;

/// <summary>Single-instance SQLite store. Commit before broadcast; retries return the same row.</summary>
public sealed class LoungeStore : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly int _retentionDays;
    private readonly TimeProvider _clock;
    private DateTimeOffset _nextPrune;

    public LoungeStore(IOptions<LoungeOptions> settings, IWebHostEnvironment environment)
        : this(Path.GetFullPath(settings.Value.DatabasePath, environment.ContentRootPath), settings.Value.RetentionDays) { }

    public LoungeStore(string path, int retentionDays = 7, TimeProvider? clock = null)
    {
        _retentionDays = retentionDays;
        _clock = clock ?? TimeProvider.System;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        _db.Open();
        using var command = _db.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT, dedup TEXT UNIQUE NOT NULL,
                clientId TEXT NOT NULL, senderId TEXT NOT NULL, name TEXT NOT NULL,
                region TEXT NOT NULL, kind TEXT NOT NULL, text TEXT NOT NULL, sentAt TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS victories (dedup TEXT PRIMARY KEY, messageId INTEGER NOT NULL, createdAt TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS messages_sentAt ON messages(sentAt);
            CREATE INDEX IF NOT EXISTS victories_createdAt ON victories(createdAt);
            CREATE INDEX IF NOT EXISTS messages_kind ON messages(kind,id);
            CREATE TABLE IF NOT EXISTS recruitments (messageId INTEGER PRIMARY KEY, state TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
    }

    public async Task<LoungeMessage[]> ReadAsync(long after = 0, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            await PruneAsync(token);
            using var command = _db.CreateCommand();
            command.CommandText = after == 0
                ? "SELECT * FROM (SELECT * FROM messages WHERE kind <> 'online' ORDER BY id DESC LIMIT 40) ORDER BY id"
                : "SELECT * FROM messages WHERE id > $after AND kind <> 'online' ORDER BY id LIMIT 200";
            command.Parameters.AddWithValue("$after", Math.Max(0, after));
            using var reader = await command.ExecuteReaderAsync(token);
            var result = new List<LoungeMessage>();
            while (await reader.ReadAsync(token)) result.Add(Read(reader));
            return result.ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<LoungeHistoryPage> ReadBeforeAsync(long before, CancellationToken token = default)
    {
        if (before <= 0) throw new ArgumentOutOfRangeException(nameof(before));
        await _gate.WaitAsync(token);
        try
        {
            await PruneAsync(token);
            using var command = _db.CreateCommand();
            command.CommandText = "SELECT * FROM messages WHERE id < $before AND kind <> 'online' ORDER BY id DESC LIMIT $limit";
            command.Parameters.AddWithValue("$before", before);
            command.Parameters.AddWithValue("$limit", LoungeRules.HistoryPageSize + 1);
            using var reader = await command.ExecuteReaderAsync(token);
            var rows = new List<LoungeMessage>();
            while (await reader.ReadAsync(token)) rows.Add(Read(reader));
            bool hasMore = rows.Count > LoungeRules.HistoryPageSize;
            return new(rows.Take(LoungeRules.HistoryPageSize).Reverse().ToArray(), hasMore);
        }
        finally { _gate.Release(); }
    }

    public async Task<LoungeMessage?> AppendAsync(string dedup, string clientId, string senderId,
        string name, string region, string kind, string text, CancellationToken token = default, string ownerId = "")
    {
        if (kind == "online") return null;
        await _gate.WaitAsync(token);
        try
        {
            await PruneAsync(token);
            using var transaction = _db.BeginTransaction();
            if (kind is "victory" or "highlight")
            {
                using var seen = _db.CreateCommand();
                seen.Transaction = transaction;
                seen.CommandText = "SELECT COUNT(*) FROM victories WHERE dedup=$key";
                seen.Parameters.AddWithValue("$key", dedup);
                if (Convert.ToInt64(await seen.ExecuteScalarAsync(token)) > 0) return null;
            }
            using var insert = _db.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT OR IGNORE INTO messages(dedup,clientId,senderId,name,region,kind,text,sentAt)
                VALUES($key,$client,$sender,$name,$region,$kind,$text,$time);
                """;
            insert.Parameters.AddWithValue("$key", dedup);
            insert.Parameters.AddWithValue("$client", clientId);
            insert.Parameters.AddWithValue("$sender", senderId);
            insert.Parameters.AddWithValue("$name", name);
            insert.Parameters.AddWithValue("$region", region);
            insert.Parameters.AddWithValue("$kind", kind);
            insert.Parameters.AddWithValue("$text", text);
            insert.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            await insert.ExecuteNonQueryAsync(token);
            using var query = _db.CreateCommand();
            query.Transaction = transaction;
            query.CommandText = "SELECT * FROM messages WHERE dedup=$key";
            query.Parameters.AddWithValue("$key", dedup);
            LoungeMessage message;
            using (var reader = await query.ExecuteReaderAsync(token))
            {
                await reader.ReadAsync(token);
                message = Read(reader);
            }
            if (kind is "victory" or "highlight")
            {
                using var victory = _db.CreateCommand();
                victory.Transaction = transaction;
                victory.CommandText = "INSERT INTO victories VALUES($key,$id,$time)";
                victory.Parameters.AddWithValue("$key", dedup);
                victory.Parameters.AddWithValue("$id", message.Id);
                victory.Parameters.AddWithValue("$time", message.SentAt.ToString("O"));
                await victory.ExecuteNonQueryAsync(token);
            }
            if (kind == "recruit" && ownerId.Length > 0)
            {
                var state = new LoungeRecruitmentState(message.Id, ownerId, message.Name,
                    LoungeContent.Parse<LoungeRecruitment>(message.Text)!, [], false, message.SentAt.AddMinutes(30));
                using var recruitment = _db.CreateCommand();
                recruitment.Transaction = transaction;
                recruitment.CommandText = "INSERT OR IGNORE INTO recruitments VALUES($id,$state)";
                recruitment.Parameters.AddWithValue("$id", message.Id);
                recruitment.Parameters.AddWithValue("$state", LoungeContent.Serialize(state));
                await recruitment.ExecuteNonQueryAsync(token);
            }
            transaction.Commit();
            return message;
        }
        finally { _gate.Release(); }
    }

    public async Task<LoungeRecruitmentState[]> ReadRecruitmentsAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            using var command = _db.CreateCommand();
            command.CommandText = "SELECT r.state FROM recruitments r JOIN messages m ON m.id=r.messageId WHERE m.kind='recruit' ORDER BY r.messageId DESC LIMIT 300";
            using var reader = await command.ExecuteReaderAsync(token);
            var result = new List<LoungeRecruitmentState>();
            while (await reader.ReadAsync(token))
                if (LoungeContent.Parse<LoungeRecruitmentState>(reader.GetString(0)) is { } state) result.Add(state);
            return result.ToArray();
        }
        finally { _gate.Release(); }
    }

    // Serialize capacity checks and membership changes with persistence, including concurrent joins.
    public async Task<LoungeRecruitmentState> ChangeRecruitmentAsync(long id, LoungeMember player,
        string action, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            using var command = _db.CreateCommand();
            command.CommandText = "SELECT r.state FROM recruitments r JOIN messages m ON m.id=r.messageId WHERE r.messageId=$id AND m.kind='recruit'";
            command.Parameters.AddWithValue("$id", id);
            var json = await command.ExecuteScalarAsync(token) as string;
            var state = json == null ? null : LoungeContent.Parse<LoungeRecruitmentState>(json);
            if (state == null) throw new ArgumentException("招募不存在或已被删除，请刷新频道。");
            if (action == "close" && player.Id != state.OwnerId) throw new ArgumentException("只有发起人可以结束招募。");
            if (action == "close" && state.Closed) return state;
            if (state.Closed || state.ExpiresAt <= DateTimeOffset.UtcNow) throw new ArgumentException("招募已结束或过期。");
            bool joined = state.Participants.Any(p => p.Id == player.Id);
            switch (action)
            {
                case "join":
                    if (player.Id == state.OwnerId) throw new ArgumentException("你是这条招募的发起人。");
                    if (joined) return state;
                    if (player.Region != state.Recruitment.Region) throw new ArgumentException("请使用招募所在大区的账号加入。");
                    if (state.Participants.Length >= state.Recruitment.Needed) throw new ArgumentException("招募已满员。");
                    state = state with { Participants = [.. state.Participants, player] };
                    break;
                case "leave":
                    if (!joined) return state;
                    state = state with { Participants = state.Participants.Where(p => p.Id != player.Id).ToArray() };
                    break;
                case "close": state = state with { Closed = true }; break;
                default: throw new ArgumentException("不支持的招募操作。");
            }
            state = state with { Revision = state.Revision + 1 };
            using var update = _db.CreateCommand();
            update.CommandText = "UPDATE recruitments SET state=$state WHERE messageId=$id";
            update.Parameters.AddWithValue("$id", id);
            update.Parameters.AddWithValue("$state", LoungeContent.Serialize(state));
            await update.ExecuteNonQueryAsync(token);
            return state;
        }
        finally { _gate.Release(); }
    }

    public async Task<long[]> ReadDeletedIdsAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            using var command = _db.CreateCommand();
            command.CommandText = "SELECT id FROM messages WHERE kind='deleted'";
            using var reader = await command.ExecuteReaderAsync(token);
            var result = new List<long>();
            while (await reader.ReadAsync(token)) result.Add(reader.GetInt64(0));
            return result.ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(long id, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            using var command = _db.CreateCommand();
            // Keep idempotency row; clients see a tombstone after reconnect too.
            command.CommandText = "UPDATE messages SET text='此消息已被管理员删除',kind='deleted' WHERE id=$id";
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync(token);
        }
        finally { _gate.Release(); }
    }

    private async Task PruneAsync(CancellationToken token)
    {
        var now = _clock.GetUtcNow();
        if (now < _nextPrune) return;
        using var command = _db.CreateCommand();
        command.CommandText = "DELETE FROM messages WHERE sentAt < $cutoff; DELETE FROM victories WHERE createdAt < $wins; DELETE FROM recruitments WHERE messageId NOT IN (SELECT id FROM messages)";
        command.Parameters.AddWithValue("$cutoff", now.AddDays(-_retentionDays).ToString("O"));
        command.Parameters.AddWithValue("$wins", now.AddDays(-30).ToString("O"));
        await command.ExecuteNonQueryAsync(token);
        // All callers hold _gate. Failed/cancelled cleanup must remain eligible for retry.
        _nextPrune = _clock.GetUtcNow().AddMinutes(1);
    }

    private static LoungeMessage Read(SqliteDataReader reader) => new(reader.GetInt64(0), reader.GetString(2),
        reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
        DateTimeOffset.Parse(reader.GetString(8), System.Globalization.CultureInfo.InvariantCulture));

    public void Dispose() { _db.Dispose(); _gate.Dispose(); }
}
