using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Domain.Skins;
using LOL_GameAssistant.Infrastructure.Skins;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class SkinCoreServiceTests
{
    private sealed class RefreshClock
    {
        private readonly System.Threading.Channels.Channel<TaskCompletionSource> _waits =
            System.Threading.Channels.Channel.CreateUnbounded<TaskCompletionSource>();
        private TaskCompletionSource? _pending;
        public Task Delay(CancellationToken token)
        {
            var wait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waits.Writer.TryWrite(wait);
            return wait.Task.WaitAsync(token);
        }
        public async Task ReleaseAsync()
        {
            var wait = _pending ?? await _waits.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            _pending = null;
            wait.SetResult();
        }
        public async Task TickAsync()
        {
            await ReleaseAsync();
            // The next delay is reached only after the entire queued refresh completes.
            _pending = await _waits.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task NewWavesKeepSelectedThemeAcrossEmptyWavesAndRestoreOriginalBaseline()
    {
        int lanes = 5;
        var clock = new RefreshClock();
        var fake = new Fake((args, _) => Task.FromResult(args[0] == "object-catalog"
            ? Objects() with { LaneCount = lanes, BaselineMinionSkin = lanes == 0 ? -1 : 2 }
            : Objects() with { ObjectTargetCount = lanes, StateVerified = lanes > 0, TransportHookReleased = true }));
        var service = new NativeSkinCoreService(fake) { ObjectRefreshDelay = clock.Delay }; service.SetEnabled(true);
        await service.ApplyObjectsAsync(Session, 4, -1);
        lanes = 0; await clock.TickAsync();
        lanes = 12; await clock.TickAsync();
        Assert.Equal(3, fake.Calls.Count(args => args[0] == "objects"));
        Assert.All(fake.Calls.Where(args => args[0] == "objects"), args => Assert.Equal(new[] { "objects", Session, "4", "-1" }, args));
        await service.RestoreObjectsAsync(Session);
        await service.ObjectMaintenanceTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "objects-restore", Session, "2", "-1" }, fake.Calls.Last());
    }

    [Fact]
    public async Task ChangingThemeReplacesPreviousAutomaticSelection()
    {
        var clock = new RefreshClock();
        var fake = new Fake((args, _) => Task.FromResult(args[0] == "object-catalog" ? Objects() :
            Objects() with { ObjectTargetCount = 6, StateVerified = true, TransportHookReleased = true }));
        var service = new NativeSkinCoreService(fake) { ObjectRefreshDelay = clock.Delay }; service.SetEnabled(true);
        await service.ApplyObjectsAsync(Session, 1, 1);
        var oldLoop = service.ObjectMaintenanceTask;
        await service.ApplyObjectsAsync(Session, 7, -1);
        await oldLoop.WaitAsync(TimeSpan.FromSeconds(5));
        // Discard the cancelled loop's pending delay, then run the new loop.
        await clock.ReleaseAsync();
        await clock.TickAsync();
        service.SetEnabled(false); await service.ObjectMaintenanceTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "objects", Session, "7", "-1" }, fake.Calls.Last());
        Assert.Equal(3, fake.Calls.Count(args => args[0] == "objects"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisableOrSessionInvalidationStopsAutomaticWrites(bool invalidate)
    {
        var clock = new RefreshClock();
        var fake = new Fake((args, _) => Task.FromResult(args[0] == "object-catalog" ? Objects() :
            Objects() with { ObjectTargetCount = 6, StateVerified = true, TransportHookReleased = true }));
        var service = new NativeSkinCoreService(fake) { ObjectRefreshDelay = clock.Delay }; service.SetEnabled(true);
        await service.ApplyObjectsAsync(Session, 1, 1);
        if (invalidate) service.InvalidateSession(); else service.SetEnabled(false);
        await service.ObjectMaintenanceTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(fake.Calls, args => args[0] == "objects");
    }

    [Fact]
    public async Task RestoreWaitsForInFlightRefreshAndNoThemeWriteFollowsRestore()
    {
        var clock = new RefreshClock();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int writes = 0;
        var fake = new Fake(async (args, token) => {
            if (args[0] == "object-catalog") return Objects();
            if (args[0] == "objects" && ++writes == 2)
            {
                Assert.False(token.CanBeCanceled);
                started.SetResult(); await release.Task;
            }
            return Objects() with { ObjectTargetCount = 6, StateVerified = true, TransportHookReleased = true };
        });
        var service = new NativeSkinCoreService(fake) { ObjectRefreshDelay = clock.Delay }; service.SetEnabled(true);
        await service.ApplyObjectsAsync(Session, 1, 1);
        await clock.ReleaseAsync(); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var restore = service.RestoreObjectsAsync(Session);
        Assert.False(restore.IsCompleted);
        release.SetResult();
        await restore.WaitAsync(TimeSpan.FromSeconds(5));
        await service.ObjectMaintenanceTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "objects", "objects", "objects-restore" },
            fake.Calls.Where(args => args[0] != "object-catalog").Select(args => args[0]));
    }

    [Theory]
    [InlineData("session")]
    [InlineData("profile")]
    [InlineData("unknown")]
    [InlineData("unverified")]
    public async Task AutomaticRefreshStopsOnSessionChangeOrUncertainWrite(string failure)
    {
        bool refresh = false;
        var clock = new RefreshClock();
        var fake = new Fake((args, _) => {
            if (args[0] == "object-catalog") return Task.FromResult(refresh ? failure switch {
                "session" => Objects() with { Session = "0000000000000000" },
                "profile" => Objects() with { ReferenceSha256 = "new" }, _ => Objects() } : Objects());
            if (refresh && failure == "unknown") throw new SkinCoreException("request_outcome_unknown", true);
            return Task.FromResult(Objects() with { ObjectTargetCount = 6, StateVerified = !refresh, TransportHookReleased = true });
        });
        var service = new NativeSkinCoreService(fake) { ObjectRefreshDelay = clock.Delay }; service.SetEnabled(true);
        await service.ApplyObjectsAsync(Session, 1, -1);
        refresh = true; await clock.ReleaseAsync();
        await service.ObjectMaintenanceTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(failure is "session" or "profile" ? 1 : 2, fake.Calls.Count(args => args[0] == "objects"));
    }

    private static SkinCoreReply Objects() => Catalog() with { LaneCount = 5, OwnWardCount = 1, BaselineMinionSkin = 0, BaselineWardSkin = 220,
        WardEntries = [new() { SkinNum = 1, Name = "Ward" }, new() { SkinNum = 220, Name = "Original" }] };
    [Fact]
    public async Task ObjectsRestoreCapturedBaselineAndDoNotReplaceItAfterApply()
    {
        bool changed = false;
        var fake = new Fake((args, _) => {
            if (args[0] == "object-catalog") return Task.FromResult(Objects() with { BaselineMinionSkin = changed ? 2 : 0, BaselineWardSkin = changed ? 1 : 220 });
            changed = true; return Task.FromResult(Objects() with { ObjectTargetCount = 6, Invoked = true, StateVerified = true, TransportHookReleased = true });
        });
        var service = new NativeSkinCoreService(fake); service.SetEnabled(true);
        Assert.True((await service.ApplyObjectsAsync(Session, 1, 1)).StateVerified);
        Assert.True((await service.RestoreObjectsAsync(Session)).StateVerified);
        Assert.Equal(new[] { "objects-restore", Session, "0", "220" }, fake.Calls.Last());
    }
    [Theory]
    [InlineData("0000000000000000", 1, 1, "stale_session")]
    [InlineData(Session, 11, 1, "invalid_object_selection")]
    [InlineData(Session, 1, 999, "invalid_object_selection")]
    public async Task InvalidObjectRequestsNeverWrite(string session, int minion, int ward, string error)
    {
        var fake = new Fake((args, _) => Task.FromResult(Objects())); var service = new NativeSkinCoreService(fake); service.SetEnabled(true);
        Assert.Equal(error, (await Assert.ThrowsAsync<SkinCoreException>(() => service.ApplyObjectsAsync(session, minion, ward))).Code);
        Assert.DoesNotContain(fake.Calls, args => args[0] == "objects");
    }
    [Fact]
    public async Task ObjectsWithNoTargetsCannotReportVerified()
    {
        var fake = new Fake((args, _) => Task.FromResult(args[0] == "object-catalog" ? Objects() : Objects() with { StateVerified = true, TransportHookReleased = true }));
        var service = new NativeSkinCoreService(fake); service.SetEnabled(true);
        Assert.False((await service.ApplyObjectsAsync(Session, 1, 1)).StateVerified);
    }
    [Fact]
    public async Task MissingObjectBaselineStopsBeforeWrite()
    {
        var fake = new Fake((args, _) => Task.FromResult(Objects() with { OwnWardCount = 0, BaselineWardSkin = -1 }));
        var service = new NativeSkinCoreService(fake); service.SetEnabled(true);
        Assert.Equal("object_baseline_unavailable", (await Assert.ThrowsAsync<SkinCoreException>(() => service.ApplyObjectsAsync(Session, -1, 1))).Code);
        Assert.DoesNotContain(fake.Calls, args => args[0] == "objects");
    }
    [Fact]
    public async Task StopRestoresObjectsBeforeHeroAndDisables()
    {
        var fake = new Fake((args, _) => Task.FromResult(args[0] switch {
            "object-catalog" => Objects(), "catalog" => Catalog(),
            "objects" or "objects-restore" => Objects() with { ObjectTargetCount = 6, StateVerified = true, Invoked = true, TransportHookReleased = true },
            _ => Applied() with { Skin = 0, ActiveSkin = 0, ActiveModel = "Lux" }
        }));
        var service = new NativeSkinCoreService(fake); service.SetEnabled(true);
        await service.ApplyObjectsAsync(Session, 1, 1); await service.StopAsync(Session);
        Assert.False(service.Enabled);
        int objects = fake.Calls.FindIndex(args => args[0] == "objects-restore"), hero = fake.Calls.FindIndex(args => args[0] == "restore");
        Assert.True(objects >= 0 && hero > objects);
    }
    private const string Session = "1234567890abcdef";
    private static SkinCoreReply Catalog() => new() { Ok = true, Independent = true, OriginalRequired = false,
        Session = Session, ReferenceSha256 = "profile", Model = "Lux", ActiveModel = "Lux", Skin = 0, ActiveSkin = 0, Entries = [
            new() { EntryId = "0:-1", Index = 0, Gear = -1, SkinNum = 0, Model = "Lux", Name = "Base" },
            new() { EntryId = "1:-1", Index = 1, Gear = -1, SkinNum = 7, Model = "Lux", Name = "Elementalist" },
            new() { EntryId = "2:-1", Index = 2, Gear = -1, SkinNum = 7, Model = "LuxFire", Name = "Fire" }] };
    private static SkinCoreReply Applied() => Catalog() with { Invoked = true, StateVerified = true, TransportHookReleased = true, Skin = 7, ActiveSkin = 7, ActiveModel = "LuxFire" };
    private sealed class Fake(Func<IReadOnlyList<string>, CancellationToken, Task<SkinCoreReply>> handler) : ISkinCoreTransport
    {
        public List<string[]> Calls { get; } = [];
        public Task<SkinCoreReply> InvokeAsync(IReadOnlyList<string> args, CancellationToken ct) { Calls.Add(args.ToArray()); return handler(args, ct); }
    }
    [Fact]
    public void CyclingWrapsAndUsesActualSpecialModelRatherThanSkinNumber()
    {
        Assert.Equal("1:-1", SkinEntryCycle.Choose(Catalog() with { Skin = 0, ActiveModel = "Lux" }, 1).EntryId);
        Assert.Equal("0:-1", SkinEntryCycle.Choose(Catalog() with { Skin = 7, ActiveModel = "LuxFire" }, 1).EntryId);
        Assert.Equal("2:-1", SkinEntryCycle.Choose(Catalog() with { Skin = 0, ActiveModel = "Lux" }, -1).EntryId);
    }
    [Fact]
    public void NativeJsonMapsSessionVersionAndVariantFields()
    {
        var reply = NativeSkinCoreTransport.ParseReply("""
            {"ok":true,"independent":true,"original_required":false,"session":"1234567890abcdef",
            "reference_sha256":"profile","model":"Lux","active_model":"LuxFire","skin":7,"active_skin":7,"gear":-1,
            "entries":[{"entry_id":"2:-1","index":2,"gear":-1,"skin_num":7,"name":"Fire","model":"LuxFire","gear_name":"Flame"}]}
            """);
        Assert.Equal("profile", reply.ReferenceSha256); Assert.False(reply.OriginalRequired);
        Assert.Equal("LuxFire", reply.ActiveModel); Assert.Equal("2:-1", Assert.Single(reply.Entries).EntryId);
        Assert.Equal(7, reply.ActiveSkin);
        Assert.Equal("Flame", reply.Entries[0].GearName); Assert.Equal(7, reply.Entries[0].SkinNum);
        Assert.True(Assert.Throws<SkinCoreException>(() => NativeSkinCoreTransport.ParseReply("null", true)).OutcomeUnknown);
    }
    [Fact]
    public void NativeFailurePreservesTransportEvidence()
    {
        var reply = NativeSkinCoreTransport.ParseReply("""
            {"ok":false,"error":"message_post_failed","stage":"message_post","win32":5,
             "pid":123,"thread_id":456,"request_id":"1234567890abcdef","phase":0,
             "host_architecture":"x64","elapsed_ms":27}
            """);
        var error = NativeSkinCoreTransport.ReplyFailure(reply, 1);
        Assert.Equal("message_post_failed", error.Code);
        Assert.False(error.OutcomeUnknown);
        Assert.Equal(5, error.Details!.Win32);
        Assert.Equal("message_post", error.Details.Stage);
        Assert.Equal(123, error.Details.Pid);
        Assert.Equal(456u, error.Details.ThreadId);
        Assert.Equal("1234567890abcdef", error.Details.RequestId);
        Assert.Equal(0, error.Details.Phase);
        Assert.Equal("x64", error.Details.HostArchitecture);
        Assert.Equal(27L, error.Details.NativeElapsedMs);
        Assert.Equal(1, error.Details.ExitCode);
    }

    [Theory]
    [InlineData("request_outcome_unknown", false)]
    [InlineData("request_expired", true)]
    public void DiagnosedFailurePreservesUnknownWriteOutcome(string code, bool invoked)
    {
        var error = NativeSkinCoreTransport.ReplyFailure(new SkinCoreReply { Error = code, Invoked = invoked }, 1);
        Assert.True(error.OutcomeUnknown);
    }

    [Fact]
    public async Task DisabledCoreNeverStartsHost()
    {
        var fake = new Fake((_, _) => Task.FromResult(Catalog())); var service = new NativeSkinCoreService(fake);
        Assert.Equal("core_disabled", (await Assert.ThrowsAsync<SkinCoreException>(() => service.GetCatalogAsync())).Code);
        Assert.Empty(fake.Calls);
    }
    [Fact]
    public async Task StaleSessionNeverWrites()
    {
        var fake = new Fake((_, _) => Task.FromResult(Catalog())); var service = new NativeSkinCoreService(fake); service.SetEnabled(true);
        Assert.Equal("stale_session", (await Assert.ThrowsAsync<SkinCoreException>(() => service.ApplyAsync("old", "2:-1"))).Code);
        Assert.Single(fake.Calls);
    }
    [Fact]
    public async Task SpecialModelsWithDuplicateSkinNumbersUseExactEntry()
    {
        var fake = new Fake((args, _) => Task.FromResult(args[0] == "catalog" ? Catalog() : Applied()));
        var service = new NativeSkinCoreService(fake); service.SetEnabled(true);
        Assert.True((await service.ApplyAsync(Session, "2:-1")).StateVerified);
        Assert.Equal(new[] { "entry", Session, "2", "-1" }, fake.Calls[1]);
    }
    [Theory]
    [InlineData("model")][InlineData("session")][InlineData("version")][InlineData("skin")][InlineData("invoked")]
    [InlineData("active_skin")][InlineData("missing_active_skin")]
    [InlineData("hook")]
    public async Task ReadbackMismatchIsNotSuccess(string mismatch)
    {
        var actual = mismatch switch { "model" => Applied() with { ActiveModel = "Lux" }, "session" => Applied() with { Session = "old" },
            "version" => Applied() with { ReferenceSha256 = "new" }, "skin" => Applied() with { Skin = 0 },
            "active_skin" => Applied() with { ActiveSkin = 0 }, "missing_active_skin" => Applied() with { ActiveSkin = null },
            "hook" => Applied() with { TransportHookReleased = false },
            _ => Applied() with { Invoked = false } };
        var service = new NativeSkinCoreService(new Fake((args, _) => Task.FromResult(args[0] == "catalog" ? Catalog() : actual))); service.SetEnabled(true);
        Assert.False((await service.ApplyAsync(Session, "2:-1")).StateVerified);
    }
    [Fact]
    public async Task DisableInvalidatesQueuedAndInFlightCatalog()
    {
        var started = new TaskCompletionSource(); var release = new TaskCompletionSource();
        var fake = new Fake(async (_, _) => { started.SetResult(); await release.Task; return Catalog(); });
        var service = new NativeSkinCoreService(fake); service.SetEnabled(true);
        var first = service.GetCatalogAsync(); await started.Task; var queued = service.ApplyAsync(Session, "2:-1");
        service.SetEnabled(false); release.SetResult();
        await Assert.ThrowsAsync<SkinCoreException>(() => first); await Assert.ThrowsAsync<SkinCoreException>(() => queued); Assert.Single(fake.Calls);
    }
    [Fact]
    public async Task DisableAfterWriteDoesNotClaimCancellationUndidTheWrite()
    {
        var started = new TaskCompletionSource(); var release = new TaskCompletionSource();
        var fake = new Fake(async (args, _) => { if (args[0] == "catalog") return Catalog(); started.SetResult(); await release.Task; return Applied(); });
        var service = new NativeSkinCoreService(fake); service.SetEnabled(true);
        var write = service.ApplyAsync(Session, "2:-1"); await started.Task; service.SetEnabled(false); release.SetResult();
        var error = await Assert.ThrowsAsync<SkinCoreException>(() => write); Assert.True(error.OutcomeUnknown);
    }
    [Fact]
    public async Task RestoreRequiresCurrentSessionAndVerifiesBase()
    {
        var fake = new Fake((args, _) => Task.FromResult(args[0] == "catalog" ? Catalog() : Applied() with { Skin = 0, ActiveSkin = 0, ActiveModel = "Lux" }));
        var service = new NativeSkinCoreService(fake); service.SetEnabled(true);
        Assert.True((await service.RestoreAsync(Session)).StateVerified); Assert.Equal(new[] { "restore", Session }, fake.Calls[1]);
    }
    [Fact]
    public async Task InvalidCatalogAndNullEntriesAreRejected()
    {
        foreach (var catalog in new[] { Catalog() with { Entries = null! }, Catalog() with { Session = null! },
            Catalog() with { Entries = [Catalog().Entries[0], Catalog().Entries[0]] }, Catalog() with { OriginalRequired = true } })
        {
            var service = new NativeSkinCoreService(new Fake((_, _) => Task.FromResult(catalog))); service.SetEnabled(true);
            await Assert.ThrowsAsync<SkinCoreException>(() => service.GetCatalogAsync());
        }
    }
    [Fact]
    public async Task UnknownOutcomeIsNeverRetried()
    {
        var fake = new Fake((args, _) => args[0] == "catalog" ? Task.FromResult(Catalog()) : throw new SkinCoreException("request_outcome_unknown", true));
        var service = new NativeSkinCoreService(fake); service.SetEnabled(true);
        await Assert.ThrowsAsync<SkinCoreException>(() => service.ApplyAsync(Session, "2:-1")); Assert.Equal(2, fake.Calls.Count);
    }
    [Theory]
    [InlineData(true)][InlineData(false)]
    public async Task StopRestoresThenDisablesEvenWhenReadbackFails(bool verified)
    {
        var fake = new Fake((args, _) => Task.FromResult(args[0] == "catalog" ? Catalog() :
            Applied() with { Skin = 0, ActiveSkin = 0, ActiveModel = "Lux", StateVerified = verified, ModulePinned = true }));
        var service = new NativeSkinCoreService(fake); service.SetEnabled(true);
        var result = await service.StopAsync(Session);
        Assert.Equal(verified, result.StateVerified); Assert.True(result.ModulePinned); Assert.False(service.Enabled);
        Assert.Equal(new[] { "restore", Session }, fake.Calls[1]);
        await Assert.ThrowsAsync<SkinCoreException>(() => service.ApplyAsync(Session, "2:-1")); Assert.Equal(2, fake.Calls.Count);
    }
    [Fact]
    public async Task StopTimeoutDisablesWithoutRetry()
    {
        var fake = new Fake((args, _) => args[0] == "catalog" ? Task.FromResult(Catalog()) : throw new SkinCoreException("request_outcome_unknown", true));
        var service = new NativeSkinCoreService(fake); service.SetEnabled(true);
        Assert.True((await Assert.ThrowsAsync<SkinCoreException>(() => service.StopAsync(Session))).OutcomeUnknown);
        Assert.False(service.Enabled); Assert.Equal(2, fake.Calls.Count);
    }
    [Fact]
    public async Task StoppingInvalidatesQueuedChangesAndRejectsNewChanges()
    {
        var started = new TaskCompletionSource(); var release = new TaskCompletionSource(); int reads = 0;
        var fake = new Fake(async (args, _) => {
            if (args[0] != "catalog") return Applied() with { Skin = 0, ActiveSkin = 0, ActiveModel = "Lux" };
            if (++reads == 1) { started.SetResult(); await release.Task; }
            return Catalog();
        });
        var service = new NativeSkinCoreService(fake); service.SetEnabled(true);
        var current = service.GetCatalogAsync(); await started.Task;
        var queued = service.ApplyAsync(Session, "2:-1"); var stop = service.StopAsync(Session);
        Assert.Equal("core_stopping", (await Assert.ThrowsAsync<SkinCoreException>(() => service.ApplyAsync(Session, "2:-1"))).Code);
        release.SetResult(); await Assert.ThrowsAsync<SkinCoreException>(() => current); await Assert.ThrowsAsync<SkinCoreException>(() => queued);
        Assert.True((await stop).StateVerified); Assert.False(service.Enabled);
        Assert.Single(fake.Calls, c => c[0] == "restore"); Assert.DoesNotContain(fake.Calls, c => c[0] == "entry");
    }

    [Fact]
    public async Task StopRestoresFirstObservedSkinInsteadOfBaseOrLatestSelection()
    {
        bool changed = false;
        var fake = new Fake((args, _) => Task.FromResult(args[0] == "catalog"
            ? Catalog() with { Skin = 7, ActiveSkin = 7, ActiveModel = changed ? "LuxFire" : "Lux" }
            : Applied() with { Skin = 7, ActiveSkin = 7, ActiveModel = args[2] == "1" ? "Lux" : "LuxFire" }));
        var service = new NativeSkinCoreService(fake); service.SetEnabled(true);
        await service.GetCatalogAsync();
        changed = true;
        await service.ApplyAsync(Session, "2:-1");
        var stopped = await service.StopAsync(Session);
        Assert.True(stopped.HeroRestoreVerified);
        Assert.Equal(new[] { "entry", Session, "1", "-1" }, fake.Calls.Last());
        Assert.DoesNotContain(fake.Calls, args => args[0] == "restore");
        Assert.False(service.Enabled);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    public async Task StopChecksModuleAfterRestoreWithoutLoadingAnotherCatalog(bool loaded, bool pinned, bool expected)
    {
        var fake = new Fake((args, _) => Task.FromResult(args[0] switch
        {
            "catalog" => Catalog(),
            "module-state" => Catalog() with { Pid = 42, GameRunning = true, ModuleLoaded = loaded, ModuleUnloadVerified = !loaded },
            _ => Applied() with { Pid = 42, Skin = 0, ActiveSkin = 0, ActiveModel = "Lux", ModulePinned = pinned }
        }));
        var service = new NativeSkinCoreService(fake); service.SetEnabled(true);
        var stopped = await service.StopAsync(Session);
        Assert.Equal(expected, stopped.ModuleUnloadVerified);
        Assert.Equal(loaded, stopped.ModuleLoaded);
        Assert.Equal(new[] { "catalog", "restore", "module-state" }, fake.Calls.Select(args => args[0]));
        Assert.Equal(new[] { "module-state", "42" }, fake.Calls.Last());
        Assert.False(service.Enabled);
    }

    [Fact]
    public async Task ModuleCheckFailureDoesNotEraseVerifiedRestoreOrClaimUnload()
    {
        var fake = new Fake((args, _) => args[0] == "module-state"
            ? throw new SkinCoreException("module_check_snapshot_failed")
            : Task.FromResult(args[0] == "catalog" ? Catalog() : Applied() with { Pid = 42, Skin = 0, ActiveSkin = 0, ActiveModel = "Lux" }));
        var service = new NativeSkinCoreService(fake); service.SetEnabled(true);
        var stopped = await service.StopAsync(Session);
        Assert.True(stopped.HeroRestoreVerified);
        Assert.Null(stopped.ModuleUnloadVerified);
        Assert.False(service.Enabled);
    }

    [Fact]
    public async Task ObjectRestoreFailureStillRestoresHeroAndReportsPartialResult()
    {
        var fake = new Fake((args, _) => args[0] == "objects-restore"
            ? throw new SkinCoreException("object_restore_not_verified", true)
            : Task.FromResult(args[0] switch
            {
                "object-catalog" => Objects(), "catalog" => Catalog(),
                "objects" => Objects() with { ObjectTargetCount = 6, StateVerified = true, Invoked = true, TransportHookReleased = true },
                _ => Applied() with { Skin = 0, ActiveSkin = 0, ActiveModel = "Lux" }
            }));
        var service = new NativeSkinCoreService(fake); service.SetEnabled(true);
        await service.ApplyObjectsAsync(Session, 1, 1);
        var stopped = await service.StopAsync(Session);
        Assert.True(stopped.HeroRestoreVerified);
        Assert.False(stopped.ObjectRestoreVerified);
        Assert.False(stopped.StateVerified);
        Assert.Contains(fake.Calls, args => args[0] == "restore");
        Assert.False(service.Enabled);
    }
}
