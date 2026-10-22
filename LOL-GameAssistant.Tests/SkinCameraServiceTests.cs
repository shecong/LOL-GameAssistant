using System.Globalization;
using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Domain.Skins;
using LOL_GameAssistant.Infrastructure.Skins;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class SkinCameraServiceTests
{
    private const string Session = "1234567890abcdef";
    private sealed class CameraTransport : ISkinCoreTransport
    {
        public float Distance = 600.25f;
        public float ViewDistance = 2250f, ViewTarget = 1800f;
        public bool ViewMismatch;
        public string CameraId = "abcdef1234567890";
        public bool Mismatch, Unknown;
        public List<string[]> Calls = [];
        public Task<SkinCoreReply> InvokeAsync(IReadOnlyList<string> args, CancellationToken token)
        {
            Calls.Add(args.ToArray());
            if (args[0] == "camera-view" && !ViewMismatch)
                ViewDistance = ViewTarget = float.Parse(args[2], CultureInfo.InvariantCulture);
            if (args[0] == "camera")
            {
                Assert.Equal(Distance.ToString("R", CultureInfo.InvariantCulture), args[3]);
                if (Unknown) throw new SkinCoreException("request_outcome_unknown", true);
                if (!Mismatch) Distance = float.Parse(args[2], CultureInfo.InvariantCulture);
            }
            return Task.FromResult(new SkinCoreReply { Ok = true, Independent = true, OriginalRequired = false,
                StateVerified = true, TransportHookReleased = true, Invoked = args[0] is "camera" or "restore" or "camera-view", Session = Session,
                CameraViewDistance = ViewDistance, CameraViewTarget = ViewTarget, CameraViewId = "aabbccdd11223344",
                ReferenceSha256 = "profile", CameraId = CameraId, CameraDistance = Distance, Model = "Lux", ActiveModel = "Lux", ActiveSkin = 0,
                Entries = [new() { EntryId = "0:-1", Model = "Lux", Name = "Base" }] });
        }
    }
    [Fact]
    public async Task MultipleChangesRestoreExactFirstValue()
    {
        var transport = new CameraTransport(); var core = new NativeSkinCoreService(transport); core.SetEnabled(true);
        await core.GetCameraAsync(); await core.SetCameraAsync(Session, 1700); await core.GetCameraAsync();
        await core.SetCameraAsync(Session, 1200); await core.RestoreCameraAsync(Session);
        Assert.Equal(600.25f, transport.Distance);
        Assert.Equal(new[] { "camera", Session, "600.25", "1200" }, transport.Calls.Last());
    }
    [Fact]
    public async Task MoveChangesCurrentAndTargetWithoutChangingConfiguration()
    {
        var transport = new CameraTransport(); var core = new NativeSkinCoreService(transport); core.SetEnabled(true);
        var moved = await core.MoveCameraAsync(Session, 600);
        Assert.Equal(600f, moved.CameraViewDistance); Assert.Equal(600f, moved.CameraViewTarget);
        Assert.Equal(600.25f, transport.Distance);
        Assert.Equal(new[] { "camera-view", Session, "600" }, transport.Calls.Last());
        await core.RestoreCameraAsync(Session);
        Assert.DoesNotContain(transport.Calls, c => c[0] == "camera");
    }
    [Fact]
    public async Task MoveRejectsStaleSessionAndUnverifiedCurrentView()
    {
        var transport = new CameraTransport(); var core = new NativeSkinCoreService(transport); core.SetEnabled(true);
        await Assert.ThrowsAsync<SkinCoreException>(() => core.MoveCameraAsync("old", 600));
        Assert.DoesNotContain(transport.Calls, c => c[0] == "camera-view");
        transport.ViewMismatch = true;
        var error = await Assert.ThrowsAsync<SkinCoreException>(() => core.MoveCameraAsync(Session, 600));
        Assert.Equal("camera_view_write_not_verified", error.Code); Assert.True(error.OutcomeUnknown);
        Assert.Single(transport.Calls, c => c[0] == "camera-view"); Assert.Equal(600.25f, transport.Distance);
    }
    [Theory]
    [InlineData(599f)] [InlineData(1701f)] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)]
    public async Task InvalidDistancesNeverStartHost(float distance)
    {
        var transport = new CameraTransport(); var core = new NativeSkinCoreService(transport); core.SetEnabled(true);
        Assert.Equal("invalid_camera_distance", (await Assert.ThrowsAsync<SkinCoreException>(() => core.SetCameraAsync(Session, distance))).Code);
        Assert.Empty(transport.Calls);
    }
    [Fact]
    public async Task DisabledAndStaleSessionsNeverWrite()
    {
        var transport = new CameraTransport(); var core = new NativeSkinCoreService(transport);
        await Assert.ThrowsAsync<SkinCoreException>(() => core.GetCameraAsync()); Assert.Empty(transport.Calls);
        core.SetEnabled(true);
        Assert.Equal("stale_session", (await Assert.ThrowsAsync<SkinCoreException>(() => core.SetCameraAsync("old", 1700))).Code);
        Assert.DoesNotContain(transport.Calls, c => c[0] == "camera");
    }
    [Fact]
    public async Task CameraReplacementDoesNotReceiveOldBaseline()
    {
        var transport = new CameraTransport(); var core = new NativeSkinCoreService(transport); core.SetEnabled(true);
        await core.SetCameraAsync(Session, 1700); transport.CameraId = "0123456789abcdef";
        Assert.Equal("camera_state_changed", (await Assert.ThrowsAsync<SkinCoreException>(() => core.RestoreCameraAsync(Session))).Code);
        Assert.Single(transport.Calls, c => c[0] == "camera");
    }
    [Fact]
    public async Task UnverifiedWritesRemainRestorableWithoutRetryingAutomatically()
    {
        var transport = new CameraTransport { Mismatch = true }; var core = new NativeSkinCoreService(transport); core.SetEnabled(true);
        Assert.True((await Assert.ThrowsAsync<SkinCoreException>(() => core.SetCameraAsync(Session, 1700))).OutcomeUnknown);
        Assert.Single(transport.Calls, c => c[0] == "camera");
        transport.Mismatch = false; await core.RestoreCameraAsync(Session); Assert.Equal(600.25f, transport.Distance);
    }
    [Fact]
    public async Task UnknownWriteKeepsBaselineForManualRestore()
    {
        var transport = new CameraTransport { Unknown = true }; var core = new NativeSkinCoreService(transport); core.SetEnabled(true);
        await Assert.ThrowsAsync<SkinCoreException>(() => core.SetCameraAsync(Session, 1700));
        transport.Unknown = false; transport.Distance = 1700;
        await core.RestoreCameraAsync(Session); Assert.Equal(600.25f, transport.Distance);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task StopRestoresCameraBeforeHeroAndReportsCameraFailure(bool failRestore)
    {
        var transport = new CameraTransport(); var core = new NativeSkinCoreService(transport); core.SetEnabled(true);
        await core.SetCameraAsync(Session, 1700); transport.Unknown = failRestore;
        var stopped = await core.StopAsync(Session);
        Assert.Equal(!failRestore, stopped.CameraRestoreVerified);
        Assert.True(stopped.HeroRestoreVerified); Assert.Equal(!failRestore, stopped.StateVerified);
        Assert.Equal(failRestore ? 1700 : 600.25f, transport.Distance);
        Assert.Equal("restore", transport.Calls.Last()[0]); Assert.False(core.Enabled);
    }
}
