using System.Text.RegularExpressions;
using LOL_GameAssistant.Domain.Skins;

namespace LOL_GameAssistant.Infrastructure.Skins;

public sealed partial class NativeSkinCoreService
{
    private string? _borderSession, _borderProfile, _borderReference, _borderBaseline;
    private bool _borderChanged;

    public Task<SkinCoreReply> GetRankBorderAsync(CancellationToken token = default) =>
        RunAsync(BorderCatalogAsync, token);

    private async Task<SkinCoreReply> BorderCatalogAsync(long revision, CancellationToken token)
    {
        var reply = await CatalogAsync(revision, token).ConfigureAwait(false);
        // No capability flag means no native writes, including for historical DLLs.
        if (!reply.RankBorderSupported) throw new SkinCoreException("rank_border_not_supported");
        if (string.IsNullOrEmpty(reply.RankBorderProfile) || !Regex.IsMatch(reply.RankBorderProfile, "\\A[a-zA-Z0-9_.:-]{1,96}\\z") ||
            reply.RankBorderEntries is not { Count: > 0 and <= 64 } ||
            reply.RankBorderEntries.Any(e => e == null || string.IsNullOrEmpty(e.Id) || !Regex.IsMatch(e.Id, "\\A[a-zA-Z0-9_.:-]{1,64}\\z") || string.IsNullOrWhiteSpace(e.Name)) ||
            reply.RankBorderEntries.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != reply.RankBorderEntries.Count ||
            !reply.RankBorderEntries.Any(e => e.Id == reply.RankBorderId))
            throw new SkinCoreException("invalid_rank_border_catalog");
        if (_borderSession != reply.Session || _borderProfile != reply.RankBorderProfile || _borderReference != reply.ReferenceSha256)
        {
            _borderSession = reply.Session; _borderProfile = reply.RankBorderProfile;
            _borderReference = reply.ReferenceSha256; _borderBaseline = reply.RankBorderId; _borderChanged = false;
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
        var result = await transport.InvokeAsync(["rank-border", session, profile, selected], token).ConfigureAwait(false);
        if (!Enabled || revision != Interlocked.Read(ref _revision)) throw new SkinCoreException("request_outcome_unknown", true);
        bool verified = result.Ok && result.Independent && !result.OriginalRequired && result.Invoked && result.StateVerified &&
            result.TransportHookReleased == true && result.RankBorderSupported && result.Session == session &&
            result.ReferenceSha256 == catalog.ReferenceSha256 && result.RankBorderProfile == profile && result.RankBorderId == selected;
        if (verified && restore) _borderChanged = false;
        Log(new { stage = "rank_border_result", result.RequestId, session, profile, selected, verified });
        return result with { StateVerified = verified, RankBorderRestoreVerified = restore ? verified : null };
    }
}
