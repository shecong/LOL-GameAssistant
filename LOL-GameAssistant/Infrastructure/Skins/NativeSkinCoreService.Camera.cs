using System.Globalization;
using System.Text.RegularExpressions;
using LOL_GameAssistant.Domain.Skins;

namespace LOL_GameAssistant.Infrastructure.Skins;

public sealed partial class NativeSkinCoreService
{
    private string? _cameraSession, _cameraId;
    private float _cameraBaseline;
    private bool _cameraChanged, _cameraViewChanged;

    public Task<SkinCoreReply> GetCameraAsync(CancellationToken token = default) => RunAsync(CameraStatusAsync, token);
    public Task<SkinCoreReply> SetCameraAsync(string session, float distance, CancellationToken token = default) =>
        RunAsync(async (revision, ct) =>
        {
            if (!ValidCameraDistance(distance)) throw new SkinCoreException("invalid_camera_distance");
            var state = await CameraStatusAsync(revision, ct).ConfigureAwait(false);
            if (state.Session != session) throw new SkinCoreException("stale_session");
            return await WriteCameraAsync(revision, state, distance, ct).ConfigureAwait(false);
        }, token);
    public Task<SkinCoreReply> RestoreCameraAsync(string session, CancellationToken token = default) =>
        RunAsync((revision, ct) => RestoreCameraInternalAsync(revision, session, ct), token);

    public Task<SkinCoreReply> MoveCameraAsync(string session, float distance, CancellationToken token = default) =>
        RunAsync(async (revision, ct) =>
        {
            if (!ValidCameraDistance(distance)) throw new SkinCoreException("invalid_camera_distance");
            var state = await CameraStatusAsync(revision, ct).ConfigureAwait(false);
            if (state.Session != session) throw new SkinCoreException("stale_session");
            return await MoveCameraViewAsync(revision, state, distance, ct).ConfigureAwait(false);
        }, token);

    private async Task<SkinCoreReply> MoveCameraViewAsync(long revision, SkinCoreReply state, float distance, CancellationToken ct)
    {
        Check(revision); ct.ThrowIfCancellationRequested();
        _cameraViewChanged = true;
        var result = await transport.InvokeAsync(["camera-view", state.Session, distance.ToString("R", CultureInfo.InvariantCulture)], ct).ConfigureAwait(false);
        CheckObjectWriteRevision(revision); ValidateCamera(result, true);
        if (!result.Invoked || result.Session != state.Session || result.CameraId != state.CameraId ||
            result.ReferenceSha256 != state.ReferenceSha256 || result.CameraDistance != state.CameraDistance ||
            !Regex.IsMatch(result.CameraViewId, "\\A[0-9a-f]{16}\\z") || result.CameraViewDistance != distance || result.CameraViewTarget != distance)
            throw new SkinCoreException("camera_view_write_not_verified", true);
        Log(new { stage = "camera_view_result", result.Session, result.CameraViewDistance, result.StateVerified });
        return result;
    }

    private static bool ValidCameraDistance(float value) => float.IsFinite(value) && value is >= 600 and <= 1700;
    private static void ValidateCamera(SkinCoreReply state, bool write = false)
    {
        if (!state.Ok || !state.Independent || state.OriginalRequired || state.TransportHookReleased != true ||
            !Regex.IsMatch(state.Session, "\\A[0-9a-f]{16}\\z") || !Regex.IsMatch(state.CameraId, "\\A[0-9a-f]{16}\\z") ||
            string.IsNullOrWhiteSpace(state.ReferenceSha256) || !ValidCameraDistance(state.CameraDistance) ||
            !float.IsFinite(state.CameraDefaultViewDistance) || state.CameraDefaultViewDistance is < 100 or > 10000 || !state.StateVerified)
            throw new SkinCoreException("invalid_camera_reply", write);
    }
    private async Task<SkinCoreReply> CameraStatusAsync(long revision, CancellationToken token)
    {
        var state = await transport.InvokeAsync(["camera-status"], token).ConfigureAwait(false);
        Check(revision); ValidateCamera(state);
        if (_cameraSession != state.Session)
        {
            _cameraSession = state.Session; _cameraId = state.CameraId;
            _cameraBaseline = state.CameraDistance; _cameraChanged = _cameraViewChanged = false;
        }
        else if (_cameraId != state.CameraId)
        {
            // Never restore a previous configuration object's baseline into a new one.
            if (_cameraChanged || _cameraViewChanged) throw new SkinCoreException("camera_state_changed");
            _cameraId = state.CameraId; _cameraBaseline = state.CameraDistance;
        }
        return state;
    }
    private async Task<SkinCoreReply> RestoreCameraInternalAsync(long revision, string session, CancellationToken token)
    {
        if (_cameraSession != session) throw new SkinCoreException("camera_baseline_unavailable");
        var state = await CameraStatusAsync(revision, token).ConfigureAwait(false);
        if (state.Session != session) throw new SkinCoreException("stale_session");
        var restored = _cameraChanged ? await WriteCameraAsync(revision, state, _cameraBaseline, token).ConfigureAwait(false) : state;
        restored = await MoveCameraViewAsync(revision, restored, state.CameraDefaultViewDistance, token).ConfigureAwait(false);
        _cameraChanged = _cameraViewChanged = false;
        return restored;
    }
    private async Task<SkinCoreReply> WriteCameraAsync(long revision, SkinCoreReply state, float distance, CancellationToken token)
    {
        Check(revision); token.ThrowIfCancellationRequested();
        _cameraChanged = true; // Retain restoration state even if a write times out.
        _cameraViewChanged = true;
        var result = await transport.InvokeAsync(["camera", state.Session, distance.ToString("R", CultureInfo.InvariantCulture),
            state.CameraDistance.ToString("R", CultureInfo.InvariantCulture)], token).ConfigureAwait(false);
        CheckObjectWriteRevision(revision); ValidateCamera(result, true);
        if (!result.Invoked || result.Session != state.Session || result.CameraId != state.CameraId ||
            result.ReferenceSha256 != state.ReferenceSha256 || result.CameraDistance != distance)
            throw new SkinCoreException("camera_write_not_verified", true);
        _cameraChanged = distance != _cameraBaseline;
        Log(new { stage = "camera_result", result.Session, result.CameraDistance, result.StateVerified });
        return result;
    }
}
