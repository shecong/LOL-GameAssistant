using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Domain.Skins;
using LOL_GameAssistant.Infrastructure.Skins;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class SkinCoreServiceTests
{
    private const string Session = "1234567890abcdef";
    private static SkinCoreReply Catalog() => new() { Ok = true, Independent = true, OriginalRequired = false,
        Session = Session, ReferenceSha256 = "profile", Model = "Lux", Entries = [
            new() { EntryId = "0:-1", Index = 0, Gear = -1, SkinNum = 0, Model = "Lux", Name = "Base" },
            new() { EntryId = "1:-1", Index = 1, Gear = -1, SkinNum = 7, Model = "Lux", Name = "Elementalist" },
            new() { EntryId = "2:-1", Index = 2, Gear = -1, SkinNum = 7, Model = "LuxFire", Name = "Fire" }] };
    private static SkinCoreReply Applied() => Catalog() with { Invoked = true, StateVerified = true, Skin = 7, ActiveModel = "LuxFire" };
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
            "reference_sha256":"profile","model":"Lux","active_model":"LuxFire","skin":7,"gear":-1,
            "entries":[{"entry_id":"2:-1","index":2,"gear":-1,"skin_num":7,"name":"Fire","model":"LuxFire","gear_name":"Flame"}]}
            """);
        Assert.Equal("profile", reply.ReferenceSha256); Assert.False(reply.OriginalRequired);
        Assert.Equal("LuxFire", reply.ActiveModel); Assert.Equal("2:-1", Assert.Single(reply.Entries).EntryId);
        Assert.Equal("Flame", reply.Entries[0].GearName); Assert.Equal(7, reply.Entries[0].SkinNum);
        Assert.True(Assert.Throws<SkinCoreException>(() => NativeSkinCoreTransport.ParseReply("null", true)).OutcomeUnknown);
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
    public async Task ReadbackMismatchIsNotSuccess(string mismatch)
    {
        var actual = mismatch switch { "model" => Applied() with { ActiveModel = "Lux" }, "session" => Applied() with { Session = "old" },
            "version" => Applied() with { ReferenceSha256 = "new" }, "skin" => Applied() with { Skin = 0 }, _ => Applied() with { Invoked = false } };
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
        var fake = new Fake((args, _) => Task.FromResult(args[0] == "catalog" ? Catalog() : Applied() with { Skin = 0, ActiveModel = "Lux" }));
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
}
