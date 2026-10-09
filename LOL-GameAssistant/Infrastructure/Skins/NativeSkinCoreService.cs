using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Domain.Skins;

namespace LOL_GameAssistant.Infrastructure.Skins;

/// <summary>One queue for all native requests; lifecycle revisions invalidate queued selections.</summary>
public sealed class NativeSkinCoreService(ISkinCoreTransport transport, string? logPath = null) : ISkinCoreService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _enabled;
    private long _revision;
    public bool Enabled => Volatile.Read(ref _enabled) == 1;
    public void SetEnabled(bool enabled) { Interlocked.Exchange(ref _enabled, enabled ? 1 : 0); InvalidateSession(); }
    public void InvalidateSession() => Interlocked.Increment(ref _revision);

    public Task<SkinCoreReply> GetCatalogAsync(CancellationToken token = default) =>
        RunAsync(async (revision, ct) => await CatalogAsync(revision, ct).ConfigureAwait(false), token);
    public Task<SkinCoreReply> ApplyAsync(string session, string entryId, CancellationToken token = default) =>
        RunAsync((revision, ct) => ApplyInternalAsync(revision, session, entryId, false, ct), token);
    public Task<SkinCoreReply> RestoreAsync(string session, CancellationToken token = default) =>
        RunAsync((revision, ct) => ApplyInternalAsync(revision, session, "", true, ct), token);

    private void Check(long revision)
    {
        if (!Enabled) throw new SkinCoreException("core_disabled");
        if (revision != Interlocked.Read(ref _revision)) throw new SkinCoreException("stale_session");
    }
    private async Task<SkinCoreReply> RunAsync(Func<long, CancellationToken, Task<SkinCoreReply>> operation, CancellationToken token)
    {
        long revision = Interlocked.Read(ref _revision); Check(revision);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { Check(revision); return await operation(revision, token).ConfigureAwait(false); }
        catch (SkinCoreException error) { Log(new { stage = "failed", error.Code, error.OutcomeUnknown }); throw; }
        finally { _gate.Release(); }
    }
    private async Task<SkinCoreReply> CatalogAsync(long revision, CancellationToken token)
    {
        var result = await transport.InvokeAsync(["catalog"], token).ConfigureAwait(false); Check(revision);
        ValidateCatalog(result); return result;
    }
    internal static void ValidateCatalog(SkinCoreReply reply)
    {
        if (!reply.Ok || !reply.Independent || reply.OriginalRequired || string.IsNullOrEmpty(reply.Session) || !Regex.IsMatch(reply.Session, "\\A[0-9a-f]{16}\\z") ||
            string.IsNullOrWhiteSpace(reply.Model) || string.IsNullOrWhiteSpace(reply.ReferenceSha256) || reply.Entries == null || reply.Entries.Count is < 1 or > 2048)
            throw new SkinCoreException("invalid_catalog");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in reply.Entries)
            if (entry == null || entry.Index is < 0 or > 999 || entry.Gear is < -1 or > 127 || entry.SkinNum < 0 ||
                entry.EntryId != $"{entry.Index}:{entry.Gear}" || string.IsNullOrWhiteSpace(entry.Model) ||
                string.IsNullOrWhiteSpace(entry.Name) || !ids.Add(entry.EntryId)) throw new SkinCoreException("invalid_catalog");
    }
    private async Task<SkinCoreReply> ApplyInternalAsync(long revision, string session, string id, bool restore, CancellationToken token)
    {
        var catalog = await CatalogAsync(revision, token).ConfigureAwait(false);
        if (session != catalog.Session) throw new SkinCoreException("stale_session");
        var candidates = restore ? catalog.Entries.Where(e => e.SkinNum == 0 && e.Gear == -1 && e.Model == catalog.Model).ToArray()
            : catalog.Entries.Where(e => e.EntryId == id).ToArray();
        if (candidates.Length != 1) throw new SkinCoreException("invalid_entry");
        var selected = candidates[0]; Check(revision); token.ThrowIfCancellationRequested();
        Log(new { stage = "request", catalog.Session, selected.EntryId, catalog.ReferenceSha256 });
        var result = await transport.InvokeAsync(restore ? ["restore", session] :
            ["entry", session, selected.Index.ToString(CultureInfo.InvariantCulture), selected.Gear.ToString(CultureInfo.InvariantCulture)], token).ConfigureAwait(false);
        // Closing/disable invalidates the presentation even if the native call completed.
        if (!Enabled || revision != Interlocked.Read(ref _revision)) throw new SkinCoreException("request_outcome_unknown", true);
        bool verified = result.Ok && result.Independent && !result.OriginalRequired && result.Invoked && result.StateVerified &&
            result.Session == session && result.ReferenceSha256 == catalog.ReferenceSha256 && result.Model == catalog.Model &&
            result.Skin == selected.SkinNum && result.ActiveModel == selected.Model && (selected.Gear < 0 || result.Gear == selected.Gear);
        result = result with { StateVerified = verified };
        Log(new { stage = "result", result.RequestId, result.Session, result.Invoked, result.StateVerified, result.Skin, result.ActiveModel, result.Gear });
        return result;
    }
    private void Log(object value)
    {
        if (logPath == null) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(logPath)!); File.AppendAllText(logPath,
            JsonSerializer.Serialize(new { time = DateTimeOffset.UtcNow, value }) + Environment.NewLine); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
