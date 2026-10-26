using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Domain.Skins;
using LOL_GameAssistant.Infrastructure.Skins;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class RankBorderTests
{
    private static SkinCoreReply Catalog(bool supported = true) => new() {
        Ok = true, Independent = true, OriginalRequired = false, Session = "1234567890abcdef", ReferenceSha256 = "fixture",
        Model = "Lux", ActiveModel = "Lux", Entries = [new() { EntryId = "0:-1", Name = "Base", Model = "Lux" }],
        RankBorderSupported = supported, RankBorderProfile = "fixture.v1", RankBorderId = "original",
        RankBorderEntries = [new() { Id = "original", Name = "Original" }, new() { Id = "selected", Name = "Selected" }]
    };
    private sealed class Transport : ISkinCoreTransport
    {
        public SkinCoreReply State = Catalog();
        public readonly List<string[]> Calls = [];
        public Func<IReadOnlyList<string>, Task<SkinCoreReply>>? Write;
        public Task<SkinCoreReply> InvokeAsync(IReadOnlyList<string> args, CancellationToken token)
        {
            Calls.Add(args.ToArray());
            if (args[0] == "rank-border-catalog") return Task.FromResult(State);
            if (Write != null) return Write(args);
            State = State with { RankBorderId = args[3], Invoked = true, StateVerified = true, TransportHookReleased = true,
                RankBorderDisplayVerified = true, RankBorderDisplayCount = 1,
                RankBorderRestoreVerified = args[3] == "original" ? true : null };
            return Task.FromResult(State);
        }
    }
    private static NativeSkinCoreService Service(Transport transport) { var service = new NativeSkinCoreService(transport); service.SetEnabled(true); return service; }

    [Fact]
    public async Task AtomicLoadingApplyRetainsNativeBaselineForRestore()
    {
        var transport = new Transport();
        // The real host accepts uppercase tier ids; use an uppercase fixture entry.
        var state = Catalog() with { RankBorderId = "CHALLENGER", RankBorderBaselineId = "original", RankBorderBaselineAvailable = true,
            Invoked = true, StateVerified = true, TransportHookReleased = true,
            RankBorderDisplayVerified = true, RankBorderDisplayCount = 1,
            RankBorderEntries = [new() { Id = "original", Name = "Original" }, new() { Id = "CHALLENGER", Name = "Challenger" }] };
        transport.Write = _ => Task.FromResult(state);
        var service = Service(transport);
        var result = await service.ApplyRankBorderForLoadingAsync("fixture.v1", "CHALLENGER");
        Assert.True(result.StateVerified);Assert.Single(transport.Calls);
        Assert.Equal("rank-border-auto", transport.Calls[0][0]);
        transport.State = state;
        transport.Write = _ => Task.FromResult(state with { RankBorderId = "original", RankBorderRestoreVerified = true });
        Assert.True((await service.RestoreRankBorderAsync(result.Session, "fixture.v1")).RankBorderRestoreVerified);
        Assert.Equal("original", transport.Calls.Last()[3]);
    }
    [Fact]
    public async Task ComponentVerificationWithoutDisplayBindingIsUnknown()
    {
        var transport = new Transport { Write = _ => Task.FromResult(Catalog() with {
            RankBorderId = "CHALLENGER", RankBorderBaselineId = "original", RankBorderBaselineAvailable = true,
            Invoked = true, StateVerified = true, TransportHookReleased = true }) };
        Assert.True((await Assert.ThrowsAsync<SkinCoreException>(() => Service(transport).ApplyRankBorderForLoadingAsync("fixture.v1", "CHALLENGER"))).OutcomeUnknown);
        Assert.Single(transport.Calls);
    }
    [Fact]
    public async Task AtomicReplyWithoutRecoverableBaselineIsUnknown()
    {
        var transport = new Transport { Write = _ => Task.FromResult(Catalog() with {
            RankBorderId = "CHALLENGER", RankBorderBaselineId = "missing", Invoked = true, StateVerified = true, TransportHookReleased = true }) };
        var error = await Assert.ThrowsAsync<SkinCoreException>(() => Service(transport).ApplyRankBorderForLoadingAsync("fixture.v1", "CHALLENGER"));
        Assert.True(error.OutcomeUnknown);Assert.Single(transport.Calls);
    }
    [Fact]
    public async Task UnsupportedCatalogNeverInvokesNativeWrite()
    {
        var transport = new Transport { State = Catalog(false) }; var service = Service(transport);
        var error = await Assert.ThrowsAsync<SkinCoreException>(() => service.ApplyRankBorderAsync("1234567890abcdef", "fixture.v1", "selected"));
        Assert.Equal("rank_border_not_supported", error.Code);
        Assert.Single(transport.Calls); Assert.Equal("rank-border-catalog", transport.Calls[0][0]);
    }
    [Theory]
    [InlineData("0000000000000000", "fixture.v1", "selected", "stale_session")]
    [InlineData("1234567890abcdef", "wrong.v1", "selected", "stale_session")]
    [InlineData("1234567890abcdef", "fixture.v1", "guessed-tier", "invalid_rank_border_entry")]
    public async Task RejectsStaleSelectionBeforeWriting(string session, string profile, string id, string code)
    {
        var transport = new Transport(); var service = Service(transport);
        Assert.Equal(code, (await Assert.ThrowsAsync<SkinCoreException>(() => service.ApplyRankBorderAsync(session, profile, id))).Code);
        Assert.Single(transport.Calls);
    }
    [Fact]
    public async Task RestoreUsesOriginalBaselineAfterRepeatedChanges()
    {
        var transport = new Transport(); var service = Service(transport);
        var catalog = await service.GetRankBorderAsync();
        Assert.True((await service.ApplyRankBorderAsync(catalog.Session, catalog.RankBorderProfile, "selected")).StateVerified);
        await service.GetRankBorderAsync();
        var restored = await service.RestoreRankBorderAsync(catalog.Session, catalog.RankBorderProfile);
        Assert.True(restored.RankBorderRestoreVerified); Assert.Equal("original", transport.Calls.Last()[3]);
    }
    [Fact]
    public async Task InvocationAloneDoesNotPassVerification()
    {
        var transport = new Transport { Write = _ => Task.FromResult(Catalog() with { Invoked = true, StateVerified = true, TransportHookReleased = true }) };
        var reply = await Service(transport).ApplyRankBorderAsync("1234567890abcdef", "fixture.v1", "selected");
        Assert.False(reply.StateVerified); // Returned actual id is still original.
    }
    [Fact]
    public async Task InvalidatedWriteIsUnknownAndNeverRetried()
    {
        var pending = new TaskCompletionSource<SkinCoreReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new Transport { Write = _ => pending.Task }; var service = Service(transport);
        var task = service.ApplyRankBorderAsync("1234567890abcdef", "fixture.v1", "selected");
        service.InvalidateSession(); pending.SetResult(Catalog() with { RankBorderId = "selected", Invoked = true, StateVerified = true });
        Assert.True((await Assert.ThrowsAsync<SkinCoreException>(() => task)).OutcomeUnknown);
        Assert.Equal(2, transport.Calls.Count);
    }
    [Fact]
    public async Task DuplicateCatalogEntriesAreRejected()
    {
        var transport = new Transport { State = Catalog() with { RankBorderEntries = [new() { Id = "original", Name = "A" }, new() { Id = "original", Name = "B" }] } };
        Assert.Equal("invalid_rank_border_catalog", (await Assert.ThrowsAsync<SkinCoreException>(() => Service(transport).GetRankBorderAsync())).Code);
    }
    [Fact]
    public void MissingCapabilityRemainsUnsupportedWhenParsingOlderReplies()
    {
        Assert.False(NativeSkinCoreTransport.ParseReply("{\"ok\":true}").RankBorderSupported);
        var reply = NativeSkinCoreTransport.ParseReply("{\"rank_border_supported\":true,\"rank_border_profile\":\"v1\",\"rank_border_entries\":[{\"id\":\"a\",\"name\":\"A\"}]}");
        Assert.True(reply.RankBorderSupported); Assert.Equal("a", reply.RankBorderEntries[0].Id);
    }
}
