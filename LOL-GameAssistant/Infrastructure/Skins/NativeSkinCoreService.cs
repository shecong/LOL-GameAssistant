using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Domain.Skins;

namespace LOL_GameAssistant.Infrastructure.Skins;

/// <summary>One queue for all native requests; lifecycle revisions invalidate queued selections.</summary>
public sealed partial class NativeSkinCoreService(ISkinCoreTransport transport, string? logPath = null) : ISkinCoreService, ISkinCoreModeSelector
{
    public SkinCoreMode Mode => (transport as ISkinCoreModeSelector)?.Mode ?? SkinCoreMode.Classic;
    public void SetMode(SkinCoreMode mode)
    {
        if (Enabled || !_gate.Wait(0)) throw new SkinCoreException("core_mode_busy");
        try
        {
            if (transport is not ISkinCoreModeSelector selector) throw new SkinCoreException("core_mode_not_supported");
            selector.SetMode(mode);
            InvalidateSession();
        }
        finally { _gate.Release(); }
    }
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _enabled;
    private int _stopping;
    private long _revision;
    private string? _objectSession;
    private int _baselineMinion = -1, _baselineWard = -1;
    private bool _minionChanged, _wardChanged;
    private string? _heroSession;
    private SkinEntry? _baselineHero;
    private readonly object _objectMaintenanceLock = new();
    private CancellationTokenSource? _objectMaintenance;
    internal Task ObjectMaintenanceTask { get; private set; } = Task.CompletedTask;
    internal Func<CancellationToken, Task> ObjectRefreshDelay { get; set; } =
        token => Task.Delay(TimeSpan.FromSeconds(2), token);
    public bool Enabled => Volatile.Read(ref _enabled) == 1;
    public void SetEnabled(bool enabled) {
        if (enabled && Volatile.Read(ref _stopping) != 0) throw new SkinCoreException("core_stopping");
        Interlocked.Exchange(ref _enabled, enabled ? 1 : 0); InvalidateSession();
    }
    public void InvalidateSession()
    {
        lock (_objectMaintenanceLock)
        {
            Interlocked.Increment(ref _revision);
            _objectMaintenance?.Cancel();
        }
    }

    private void StopObjectMaintenance()
    {
        lock (_objectMaintenanceLock) _objectMaintenance?.Cancel();
    }

    private void StartObjectMaintenance(long revision, SkinCoreReply catalog, int minion, int ward)
    {
        lock (_objectMaintenanceLock)
        {
            Check(revision);
            _objectMaintenance?.Cancel();
            var cancellation = new CancellationTokenSource();
            _objectMaintenance = cancellation;
            ObjectMaintenanceTask = MaintainObjectsAsync(revision, catalog, minion, ward, cancellation);
        }
    }

    private async Task MaintainObjectsAsync(long revision, SkinCoreReply appliedCatalog, int minion, int ward,
        CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            while (true)
            {
                await ObjectRefreshDelay(token).ConfigureAwait(false);
                // Use the same queue as manual changes and restoration. Cancelling the
                // loop stops queued work, but never interrupts a native write in flight.
                await RunAsync(async (_, _) =>
                {
                    Check(revision); token.ThrowIfCancellationRequested();
                    var catalog = await ObjectCatalogAsync(revision, token).ConfigureAwait(false);
                    if (catalog.Session != appliedCatalog.Session || catalog.ReferenceSha256 != appliedCatalog.ReferenceSha256)
                        throw new SkinCoreException("stale_session");
                    Check(revision); token.ThrowIfCancellationRequested();
                    var result = await transport.InvokeAsync(["objects", catalog.Session,
                        minion.ToString(CultureInfo.InvariantCulture), ward.ToString(CultureInfo.InvariantCulture)], CancellationToken.None).ConfigureAwait(false);
                    CheckObjectWriteRevision(revision);
                    result = ValidateObjectResult(result, catalog);
                    // No objects between waves is normal; do not recapture the baseline.
                    if (result.ObjectTargetCount > 0 && !result.StateVerified)
                        throw new SkinCoreException("object_refresh_not_verified", true);
                    return result;
                }, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            // A failed/uncertain write must not turn into an automatic retry loop.
            Log(new { stage = "object_maintenance_stopped", error = error.Message });
        }
        finally
        {
            lock (_objectMaintenanceLock)
            {
                if (ReferenceEquals(_objectMaintenance, cancellation)) _objectMaintenance = null;
                cancellation.Dispose();
            }
        }
    }

    public Task<SkinCoreReply> GetCatalogAsync(CancellationToken token = default) =>
        RunAsync(async (revision, ct) => await CatalogAsync(revision, ct).ConfigureAwait(false), token);
    public Task<SkinCoreReply> ApplyAsync(string session, string entryId, CancellationToken token = default) =>
        RunAsync((revision, ct) => ApplyInternalAsync(revision, session, entryId, false, ct), token);
    public Task<SkinCoreReply> RestoreAsync(string session, CancellationToken token = default) =>
        RunAsync((revision, ct) => ApplyInternalAsync(revision, session, "", true, ct), token);

    public async Task<SkinCoreReply> StopAsync(string session, CancellationToken token = default)
    {
        if (Interlocked.CompareExchange(ref _stopping, 1, 0) != 0) throw new SkinCoreException("core_stopping");
        InvalidateSession(); long revision = Interlocked.Read(ref _revision); bool acquired = false;
        try {
            await _gate.WaitAsync(token).ConfigureAwait(false); acquired = true; Check(revision);
            bool borderRestored = true;
            if (_borderSession == session && _borderChanged)
            {
                try { borderRestored = (await ChangeBorderAsync(revision, session, _borderProfile!, "", true, token).ConfigureAwait(false)).StateVerified; }
                catch (SkinCoreException error) { borderRestored = false; Log(new { stage = "stop_rank_border_restore_failed", error.Code, error.OutcomeUnknown }); }
            }
            bool cameraRestored = true;
            if (_cameraSession == session && (_cameraChanged || _cameraViewChanged))
            {
                try { await RestoreCameraInternalAsync(revision, session, token).ConfigureAwait(false); }
                catch (SkinCoreException error) { cameraRestored = false; Log(new { stage = "stop_camera_restore_failed", error.Code, error.OutcomeUnknown }); }
            }
            bool objectsRestored = true;
            if (_objectSession == session && (_minionChanged || _wardChanged))
            {
                try { await RestoreObjectsInternalAsync(revision, session, token).ConfigureAwait(false); }
                catch (SkinCoreException error) { objectsRestored = false; Log(new { stage = "stop_object_restore_failed", error.Code, error.OutcomeUnknown }); }
            }
            var catalog = await CatalogAsync(revision, token).ConfigureAwait(false);
            if (catalog.Session != session) throw new SkinCoreException("stale_session");
            if (_baselineHero == null) throw new SkinCoreException("hero_baseline_unavailable");
            bool baseSkin = _baselineHero.SkinNum == 0 && _baselineHero.Gear == -1 && _baselineHero.Model == catalog.Model;
            var restored = await ApplyInternalAsync(revision, session, _baselineHero.EntryId, baseSkin, token, catalog).ConfigureAwait(false);
            if (restored.StateVerified && restored.TransportHookReleased == true && restored.Pid > 0)
            {
                try
                {
                    var state = await transport.InvokeAsync(["module-state", restored.Pid.ToString(CultureInfo.InvariantCulture)], token).ConfigureAwait(false);
                    bool verified = state.Ok && state.Independent && !state.OriginalRequired && state.Pid == restored.Pid &&
                        state.ModuleLoaded == false && state.ModuleUnloadVerified == true;
                    restored = restored with { GameRunning = state.GameRunning, ModuleLoaded = state.ModuleLoaded, ModuleUnloadVerified = verified };
                    Log(new { stage = "stop_module_state", restored.Pid, restored.GameRunning, restored.ModuleLoaded, restored.ModuleUnloadVerified });
                }
                catch (SkinCoreException error) { Log(new { stage = "stop_module_check_failed", error.Code }); }
            }
            return restored with { HeroRestoreVerified = restored.StateVerified, ObjectRestoreVerified = objectsRestored, CameraRestoreVerified = cameraRestored,
                RankBorderRestoreVerified = borderRestored, StateVerified = restored.StateVerified && objectsRestored && cameraRestored && borderRestored };
        }
        catch (SkinCoreException error) { Log(new { stage = "stop_failed", error.Code, error.OutcomeUnknown }); throw; }
        finally {
            // Even an uncertain restore must not resume polling or queued skin changes.
            SetEnabled(false); Volatile.Write(ref _stopping, 0);
            if (acquired) _gate.Release();
        }
    }

    public Task<SkinCoreReply> GetObjectCatalogAsync(CancellationToken token = default) => RunAsync(ObjectCatalogAsync, token);
    public Task<SkinCoreReply> ApplyObjectsAsync(string session, int minionIndex, int wardSkin, CancellationToken token = default) =>
        RunAsync(async (revision, ct) => {
            var catalog = await ObjectCatalogAsync(revision, ct).ConfigureAwait(false);
            if (catalog.Session != session) throw new SkinCoreException("stale_session");
            if (minionIndex is < -1 or > 10 || wardSkin < -1 || (minionIndex < 0 && wardSkin < 0) ||
                (wardSkin >= 0 && !catalog.WardEntries.Any(e => e.SkinNum == wardSkin))) throw new SkinCoreException("invalid_object_selection");
            if ((minionIndex >= 0 && (catalog.LaneCount == 0 || _baselineMinion < 0)) ||
                (wardSkin >= 0 && (catalog.OwnWardCount == 0 || _baselineWard < 0))) throw new SkinCoreException("object_baseline_unavailable");
            Check(revision); ct.ThrowIfCancellationRequested();
            StopObjectMaintenance();
            _minionChanged |= minionIndex >= 0; _wardChanged |= wardSkin >= 0;
            var result = await transport.InvokeAsync(["objects", session, minionIndex.ToString(CultureInfo.InvariantCulture), wardSkin.ToString(CultureInfo.InvariantCulture)], ct).ConfigureAwait(false);
            CheckObjectWriteRevision(revision); result = ValidateObjectResult(result, catalog);
            if (result.StateVerified) StartObjectMaintenance(revision, catalog, minionIndex, wardSkin);
            return result;
        }, token);
    public Task<SkinCoreReply> RestoreObjectsAsync(string session, CancellationToken token = default) =>
        RunAsync((revision, ct) => RestoreObjectsInternalAsync(revision, session, ct), token);
    private async Task<SkinCoreReply> ObjectCatalogAsync(long revision, CancellationToken token)
    {
        var catalog = await transport.InvokeAsync(["object-catalog"], token).ConfigureAwait(false); Check(revision);
        if (!catalog.Ok || !catalog.Independent || catalog.OriginalRequired || !Regex.IsMatch(catalog.Session, "\\A[0-9a-f]{16}\\z") ||
            string.IsNullOrWhiteSpace(catalog.ReferenceSha256) || catalog.LaneCount is < 0 or > 10000 || catalog.OwnWardCount is < 0 or > 10000 ||
            catalog.BaselineMinionSkin is < -1 or > 10000 || catalog.BaselineWardSkin is < -1 or > 1000 ||
            catalog.WardEntries.Count > 1001 || catalog.WardEntries.Any(e => e.SkinNum is < 0 or > 1000 || string.IsNullOrWhiteSpace(e.Name)) ||
            catalog.WardEntries.Select(e => e.SkinNum).Distinct().Count() != catalog.WardEntries.Count) throw new SkinCoreException("invalid_object_catalog");
        if (_objectSession != catalog.Session) { _objectSession = catalog.Session; _baselineMinion = _baselineWard = -1; _minionChanged = _wardChanged = false; }
        if (_baselineMinion < 0 && !_minionChanged) _baselineMinion = catalog.BaselineMinionSkin;
        if (_baselineWard < 0 && !_wardChanged) _baselineWard = catalog.BaselineWardSkin;
        return catalog;
    }
    private async Task<SkinCoreReply> RestoreObjectsInternalAsync(long revision, string session, CancellationToken token)
    {
        StopObjectMaintenance();
        var catalog = await ObjectCatalogAsync(revision, token).ConfigureAwait(false);
        if (session != catalog.Session) throw new SkinCoreException("stale_session");
        var result = await transport.InvokeAsync(["objects-restore", session, (_minionChanged ? _baselineMinion : -1).ToString(CultureInfo.InvariantCulture),
            (_wardChanged ? _baselineWard : -1).ToString(CultureInfo.InvariantCulture)], token).ConfigureAwait(false);
        CheckObjectWriteRevision(revision); result = ValidateObjectResult(result, catalog);
        if (!result.StateVerified && result.ObjectTargetCount != 0) throw new SkinCoreException("object_restore_not_verified", true);
        if (result.StateVerified || result.ObjectTargetCount == 0) { _minionChanged = _wardChanged = false; }
        return result;
    }
    private SkinCoreReply ValidateObjectResult(SkinCoreReply result, SkinCoreReply catalog)
    {
        if (!result.Ok || !result.Independent || result.OriginalRequired || result.TransportHookReleased != true ||
            result.Session != catalog.Session || result.ReferenceSha256 != catalog.ReferenceSha256 ||
            result.ObjectTargetCount is < 0 or > 10000 || result.ObjectChangedCount < 0 || result.ObjectChangedCount > result.ObjectTargetCount)
            throw new SkinCoreException("invalid_object_result", true);
        bool verified = result.Ok && result.Independent && !result.OriginalRequired && result.StateVerified && result.ObjectTargetCount > 0 &&
            result.TransportHookReleased == true && result.Session == catalog.Session && result.ReferenceSha256 == catalog.ReferenceSha256;
        Log(new { stage = "objects_result", result.RequestId, result.Session, result.ObjectTargetCount, result.ObjectChangedCount, verified });
        return result with { StateVerified = verified };
    }
    private void CheckObjectWriteRevision(long revision)
    {
        if (!Enabled || revision != Interlocked.Read(ref _revision)) throw new SkinCoreException("request_outcome_unknown", true);
    }

    private void Check(long revision)
    {
        if (!Enabled) throw new SkinCoreException("core_disabled");
        if (revision != Interlocked.Read(ref _revision)) throw new SkinCoreException("stale_session");
    }
    private async Task<SkinCoreReply> RunAsync(Func<long, CancellationToken, Task<SkinCoreReply>> operation, CancellationToken token)
    {
        if (Volatile.Read(ref _stopping) != 0) throw new SkinCoreException("core_stopping");
        long revision = Interlocked.Read(ref _revision); Check(revision);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { Check(revision); if (Volatile.Read(ref _stopping) != 0) throw new SkinCoreException("core_stopping"); return await operation(revision, token).ConfigureAwait(false); }
        catch (SkinCoreException error) { Log(new { stage = "failed", error.Code, error.OutcomeUnknown }); throw; }
        finally { _gate.Release(); }
    }
    private async Task<SkinCoreReply> CatalogAsync(long revision, CancellationToken token)
    {
        var result = await transport.InvokeAsync(["catalog"], token).ConfigureAwait(false); Check(revision);
        ValidateCatalog(result);
        if (_heroSession != result.Session)
        {
            _heroSession = result.Session;
            int skin = result.ActiveSkin ?? result.Skin;
            _baselineHero = result.Entries.FirstOrDefault(entry => entry.SkinNum == skin &&
                entry.Model == result.ActiveModel && entry.Gear == result.Gear);
        }
        return result;
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
    private async Task<SkinCoreReply> ApplyInternalAsync(long revision, string session, string id, bool restore, CancellationToken token,
        SkinCoreReply? validatedCatalog = null)
    {
        var catalog = validatedCatalog ?? await CatalogAsync(revision, token).ConfigureAwait(false);
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
        bool verified = result.Ok && result.Independent && !result.OriginalRequired && result.Invoked && result.StateVerified && result.TransportHookReleased == true &&
            result.Session == session && result.ReferenceSha256 == catalog.ReferenceSha256 && result.Model == catalog.Model &&
            result.Skin == selected.SkinNum && result.ActiveSkin == selected.SkinNum && result.ActiveModel == selected.Model && (selected.Gear < 0 || result.Gear == selected.Gear);
        result = result with { StateVerified = verified };
        Log(new { stage = "result", result.RequestId, result.Session, result.Invoked, result.StateVerified, result.Skin, result.ActiveSkin, result.ActiveModel, result.Gear, result.TransportHookReleased, result.ModulePinned });
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
