using System.Text.RegularExpressions;
using LOL_GameAssistant.Domain.Skins;

namespace LOL_GameAssistant.Infrastructure.Skins;

public sealed partial class NativeSkinCoreService
{
    private string? _borderSession, _borderProfile, _borderReference, _borderBaseline;
    private bool _borderChanged, _borderRecoveryNeeded;

    public Task<SkinCoreReply> GetRankBorderAsync(CancellationToken token = default) =>
        RunAsync(BorderCatalogAsync, token);

    private async Task<SkinCoreReply> BorderCatalogAsync(long revision, CancellationToken token)
    {
        Check(revision);
        var reply = await transport.InvokeAsync(["rank-border-catalog"], token).ConfigureAwait(false);
        Check(revision);token.ThrowIfCancellationRequested();
        // No capability flag means no native writes, including for historical DLLs.
        if (!reply.RankBorderSupported) throw new SkinCoreException("rank_border_not_supported");
        if (!reply.Ok || !reply.Independent || reply.OriginalRequired || !Regex.IsMatch(reply.Session, "\\A[0-9a-f]{16}\\z"))
            throw new SkinCoreException("invalid_rank_border_catalog");
        if (string.IsNullOrEmpty(reply.RankBorderProfile) || !Regex.IsMatch(reply.RankBorderProfile, "\\A[a-zA-Z0-9_.:-]{1,96}\\z") ||
            reply.RankBorderEntries is not { Count: > 0 and <= 64 } ||
            reply.RankBorderEntries.Any(e => e == null || string.IsNullOrEmpty(e.Id) || !Regex.IsMatch(e.Id, "\\A[a-zA-Z0-9_.:-]{1,64}\\z") || string.IsNullOrWhiteSpace(e.Name)) ||
            reply.RankBorderEntries.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != reply.RankBorderEntries.Count ||
            !reply.RankBorderEntries.Any(e => e.Id == reply.RankBorderId))
            throw new SkinCoreException("invalid_rank_border_catalog");
        if (_borderSession != reply.Session || _borderProfile != reply.RankBorderProfile || _borderReference != reply.ReferenceSha256)
        {
            _borderSession = reply.Session; _borderProfile = reply.RankBorderProfile;
            _borderReference = reply.ReferenceSha256;
            _borderBaseline = reply.RankBorderEntries.Any(entry => entry.Id == reply.RankBorderBaselineId) ? reply.RankBorderBaselineId : reply.RankBorderId;
            _borderChanged = reply.RankBorderBaselineAvailable;_borderRecoveryNeeded = false;
        }
        return reply;
    }

    public Task<SkinCoreReply> ApplyRankBorderAsync(string session, string profile, string id, CancellationToken token = default) =>
        RunAsync((revision, ct) => ChangeBorderAsync(revision, session, profile, id, false, ct), token);

    public Task<SkinCoreReply> RestoreRankBorderAsync(string session, string profile, CancellationToken token = default) =>
        RunAsync((revision, ct) => ChangeBorderAsync(revision, session, profile, "", true, ct), token);

    private async Task<SkinCoreReply> ChangeBorderAsync(long revision, string session, string profile, string id, bool restore, CancellationToken token)
    {
        var catalog = await BorderCatalogAsync(revision, token).ConfigureAwait(false);
        if (catalog.Session != session || catalog.RankBorderProfile != profile) throw new SkinCoreException("stale_session");
        string selected = restore ? _borderBaseline ?? "" : id;
        if (!catalog.RankBorderEntries.Any(e => e.Id == selected)) throw new SkinCoreException("invalid_rank_border_entry");
        Check(revision); token.ThrowIfCancellationRequested();
        // Remember a potentially executed write even when verification/transport fails.
        _borderChanged = true;
        Log(new { stage = "rank_border_request", session, profile, selected, restore });
        var result = await transport.InvokeAsync(["rank-border", session, profile, restore ? "original" : selected], token).ConfigureAwait(false);
        if (!Enabled || revision != Interlocked.Read(ref _revision)) throw new SkinCoreException("request_outcome_unknown", true);
        bool verified = result.Ok && result.Independent && !result.OriginalRequired && result.Invoked && result.StateVerified &&
            result.TransportHookReleased == true && result.RankBorderSupported && result.Session == session &&
            result.ReferenceSha256 == catalog.ReferenceSha256 && result.RankBorderProfile == profile && result.RankBorderId == selected &&
            (result.RankBorderDisplayVerified == true && result.RankBorderDisplayCount is > 0 and <= 16 ||
                restore && result.RankBorderDisplayState == "not_present" && result.RankBorderDisplayCount == 0) &&
            (!restore || result.RankBorderRestoreVerified == true);
        if (verified && restore) _borderChanged = false;
        Log(new { stage = "rank_border_result", result.RequestId, session, profile, selected, verified,
            result.RankBorderDisplayVerified, result.RankBorderDisplayCount, result.RankBorderDisplayState });
        return result with { StateVerified = verified, RankBorderRestoreVerified = restore ? verified : null };
    }

    public Task<SkinCoreReply> ApplyRankBorderForLoadingAsync(string profile, string id, CancellationToken token = default) =>
        RunAsync(async (revision, ct) =>
        {
            if (!Regex.IsMatch(profile, "\\A[a-zA-Z0-9_.:-]{1,96}\\z") || !Regex.IsMatch(id, "\\A[A-Z]{1,20}\\z"))
                throw new SkinCoreException("invalid_rank_border_entry");
            Check(revision);ct.ThrowIfCancellationRequested();
            SkinCoreReply result;
            try { result = await transport.InvokeAsync(["rank-border-auto", profile, id], ct).ConfigureAwait(false); }
            catch (SkinCoreException error) when (error.OutcomeUnknown) { _borderRecoveryNeeded = true;throw; }
            bool verified = Enabled && revision == Interlocked.Read(ref _revision) && result.Ok && result.Independent && !result.OriginalRequired &&
                result.Invoked && result.StateVerified && result.TransportHookReleased == true && result.RankBorderSupported && result.RankBorderBaselineAvailable &&
                result.RankBorderDisplayVerified == true && result.RankBorderDisplayCount is > 0 and <= 16 &&
                result.RankBorderProfile == profile && result.RankBorderId == id && Regex.IsMatch(result.Session, "\\A[0-9a-f]{16}\\z") &&
                result.RankBorderEntries.Any(entry => entry.Id == result.RankBorderBaselineId);
            if (!verified) { _borderRecoveryNeeded = true;throw new SkinCoreException("request_outcome_unknown", true); }
            _borderSession = result.Session;_borderProfile = profile;_borderReference = result.ReferenceSha256;
            _borderBaseline = result.RankBorderBaselineId;_borderChanged = true;_borderRecoveryNeeded = false;
            Log(new { stage = "rank_border_loading_applied", result.Session, id, result.Pid,
                result.RankBorderDisplayVerified, result.RankBorderDisplayCount, visualConfirmed = false });
            return result;
        }, token);
}
