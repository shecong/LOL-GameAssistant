using System.IO.Compression;
using System.Text.Json;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Infrastructure.GameData;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

internal sealed partial class CachedMatchHistoryService
{
    private sealed record StoredCompleteHistory(byte[] Content);
    private LocalDataCache<StoredCompleteHistory> _completeHistory = null!;

    public async Task<CompletePlayerHistorySnapshot?> ReadCompleteHistoryAsync(string puuid, CancellationToken token)
    {
        string session = PlayerHistorySession.Key;
        string scope = await _context(token).ConfigureAwait(false);
        if (session != PlayerHistorySession.Key) throw new OperationCanceledException("客户端会话已变化。");
        var stored = await _completeHistory.GetAsync($"v1:{scope}:{puuid}",
            () => Task.FromResult<StoredCompleteHistory?>(null), token).ConfigureAwait(false);
        if (stored == null) return null;
        var snapshot = await Task.Run(async () =>
        {
            try
            {
                using var input = new MemoryStream(stored.Content);
                using var zip = new GZipStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                byte[] buffer = new byte[65536];
                int read;
                while ((read = await zip.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    if (output.Length + read > 128 * 1024 * 1024) return null;
                    output.Write(buffer, 0, read);
                }
                return JsonSerializer.Deserialize<CompletePlayerHistorySnapshot>(output.GetBuffer().AsSpan(0, (int)output.Length));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException) { return null; }
        }, token).ConfigureAwait(false);
        if (session != PlayerHistorySession.Key) throw new OperationCanceledException("客户端会话已变化。");
        return snapshot?.Data.Games is { IsTotalKnown: true, HasMore: false } ? snapshot : null;
    }

    public async Task SaveCompleteHistoryAsync(string puuid, CompletePlayerHistorySnapshot snapshot, CancellationToken token)
    {
        if (snapshot.Data.Games is not { IsTotalKnown: true, HasMore: false }) return;
        string session = PlayerHistorySession.Key;
        string scope = await _context(token).ConfigureAwait(false);
        var stored = await Task.Run(() =>
        {
            using var output = new MemoryStream();
            using (var zip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
                JsonSerializer.Serialize(zip, snapshot);
            return new StoredCompleteHistory(output.ToArray());
        }, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (session != PlayerHistorySession.Key) throw new OperationCanceledException("客户端会话已变化。");
        await _completeHistory.GetLatestOrCachedAsync($"v1:{scope}:{puuid}",
            () => Task.FromResult<StoredCompleteHistory?>(stored), token).ConfigureAwait(false);
    }
}
