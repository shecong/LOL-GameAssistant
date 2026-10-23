using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Domain.Skins;
using LOL_GameAssistant.Infrastructure.Skins;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class SkinCoreModeTests
{
    private sealed class Transport(Func<Task<SkinCoreReply>> invoke) : ISkinCoreTransport
    {
        public int Calls;
        public Task<SkinCoreReply> InvokeAsync(IReadOnlyList<string> args, CancellationToken token) { Calls++; return invoke(); }
    }
    [Fact]
    public async Task ExplicitSelectionRoutesWithoutFallbackEvenOnUnknownWrite()
    {
        var classic = new Transport(() => Task.FromResult(new SkinCoreReply()));
        var hybrid = new Transport(() => throw new SkinCoreException("request_outcome_unknown", true));
        var router = new SelectableSkinCoreTransport(classic, hybrid);
        await router.InvokeAsync(["catalog"], default);
        router.SetMode(SkinCoreMode.KernelRelay);
        Assert.True((await Assert.ThrowsAsync<SkinCoreException>(() => router.InvokeAsync(["entry"], default))).OutcomeUnknown);
        Assert.Equal(1, classic.Calls); Assert.Equal(1, hybrid.Calls);
        router.SetMode(SkinCoreMode.Classic);
        await router.InvokeAsync(["catalog"], default);
        Assert.Equal(2, classic.Calls);
    }
    [Fact]
    public async Task InFlightRequestPreventsModeSwitchAndKeepsOriginalBackend()
    {
        var pending = new TaskCompletionSource<SkinCoreReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var classic = new Transport(() => pending.Task);
        var hybrid = new Transport(() => Task.FromResult(new SkinCoreReply()));
        var router = new SelectableSkinCoreTransport(classic, hybrid);
        var request = router.InvokeAsync(["catalog"], default);
        Assert.Equal("core_mode_busy", Assert.Throws<SkinCoreException>(() => router.SetMode(SkinCoreMode.KernelRelay)).Code);
        pending.SetResult(new()); await request;
        router.SetMode(SkinCoreMode.KernelRelay);
        Assert.Equal(SkinCoreMode.KernelRelay, router.Mode); Assert.Equal(0, hybrid.Calls);
    }
    [Fact]
    public void ServiceRequiresDisabledCoreAndRejectsInvalidModes()
    {
        var transport = new Transport(() => Task.FromResult(new SkinCoreReply()));
        var router = new SelectableSkinCoreTransport(transport, transport, (SkinCoreMode)99);
        var service = new NativeSkinCoreService(router);
        Assert.Equal(SkinCoreMode.Classic, service.Mode);
        service.SetEnabled(true);
        Assert.Equal("core_mode_busy", Assert.Throws<SkinCoreException>(() => service.SetMode(SkinCoreMode.KernelRelay)).Code);
        service.SetEnabled(false); service.SetMode(SkinCoreMode.KernelRelay);
        Assert.Equal(SkinCoreMode.KernelRelay, service.Mode);
        Assert.Equal("invalid_core_mode", Assert.Throws<SkinCoreException>(() => service.SetMode((SkinCoreMode)100)).Code);
    }
}
