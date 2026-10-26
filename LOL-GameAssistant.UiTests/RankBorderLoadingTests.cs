using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Domain.Skins;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class RankBorderLoadingTests
{
    private sealed class Clock : TimeProvider { internal DateTimeOffset Now = DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow() => Now; }
    private static AssistantSettings Config() => new() { SkinCoreEnabled = true, RankBorderAutoApplyEnabled = true,
        PreferredRankBorderId = "CHALLENGER", PreferredRankBorderProfile = RankBorderPresets.Profile };
    [Fact]
    public async Task LoadingWaitsArePolledButSuccessfulWriteIsNotRepeated()
    {
        int calls = 0;bool enabled = false;
        var core = SecondPhaseRegressionTests.Service<ISkinCoreService>((method, args) => method.Name switch
        {
            "get_Enabled" => enabled,
            "SetEnabled" => SetEnabled(),
            "ApplyRankBorderForLoadingAsync" => ++calls < 3
                ? Task.FromException<SkinCoreReply>(new SkinCoreException("rank_border_not_ready"))
                : Task.FromResult(new SkinCoreReply { StateVerified = true }),
            _ => null
        });
        object? SetEnabled() { enabled = true;return null; }
        using var coordinator = new RankBorderLoadingCoordinator(core, Config, () => 1);
        var updates = new List<RankBorderLoadingUpdate>();coordinator.Changed += updates.Add;
        for(int i=0;i<8;i++) await coordinator.TickAsync(1, Config());
        Assert.Equal(3, calls);Assert.True(enabled);Assert.Equal(2, updates.Count);
        await coordinator.TickAsync(2, Config());Assert.Equal(4, calls);
    }
    [Theory]
    [InlineData("core_response_timeout", false)]
    [InlineData("request_outcome_unknown", true)]
    [InlineData("rank_border_not_ready", true)]
    [InlineData("rank_border_profile_mismatch", false)]
    public async Task UncertainOrPermanentFailuresNeverRetry(string code, bool unknown)
    {
        int calls=0;
        var core = SecondPhaseRegressionTests.Service<ISkinCoreService>((method, _) =>
        {
            if(method.Name=="get_Enabled") return true;
            if(method.Name=="ApplyRankBorderForLoadingAsync") {calls++;return Task.FromException<SkinCoreReply>(new SkinCoreException(code, unknown));}
            return null;
        });
        using var coordinator = new RankBorderLoadingCoordinator(core, Config, () => 1);
        await coordinator.TickAsync(1, Config());await coordinator.TickAsync(1, Config());Assert.Equal(1,calls);
    }
    [Fact]
    public async Task DisabledFeatureAndInvalidPreferenceNeverReachNativeCode()
    {
        int calls=0;
        var core = SecondPhaseRegressionTests.Service<ISkinCoreService>((_, _) => {calls++;return null;});
        using var coordinator = new RankBorderLoadingCoordinator(core, Config, () => 1);
        var config=Config();config.RankBorderAutoApplyEnabled=false;await coordinator.TickAsync(1,config);
        config=Config();config.SkinCoreEnabled=false;await coordinator.TickAsync(1,config);
        config=Config();config.PreferredRankBorderId="invalid";await coordinator.TickAsync(2,config);
        Assert.Equal(0,calls);
    }
    [Fact]
    public async Task LoadingWindowExpiresInsteadOfPollingThroughoutTheGame()
    {
        int calls=0;var clock=new Clock();
        var core = SecondPhaseRegressionTests.Service<ISkinCoreService>((method, _) =>
        {
            if(method.Name=="get_Enabled")return true;
            if(method.Name=="ApplyRankBorderForLoadingAsync") {calls++;return Task.FromException<SkinCoreReply>(new SkinCoreException("rank_border_loading_assets_unavailable"));}
            return null;
        });
        using var coordinator=new RankBorderLoadingCoordinator(core,Config,()=>1,clock);
        await coordinator.TickAsync(1,Config());clock.Now=clock.Now.AddSeconds(46);
        await coordinator.TickAsync(1,Config());await coordinator.TickAsync(1,Config());Assert.Equal(1,calls);
    }
}
