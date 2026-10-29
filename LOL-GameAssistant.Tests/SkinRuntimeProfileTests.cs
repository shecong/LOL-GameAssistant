using LOL_GameAssistant.Infrastructure.Skins;
using LOL_GameAssistant.Domain.Skins;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class SkinRuntimeProfileTests
{
    [Fact]
    public void LocatorFailureKeepsSpecificStageAndRuleIdentity()
    {
        var reply = NativeSkinCoreTransport.ParseReply("""
            {"ok":false,"error":"profile_signature_missing","stage":"locator:skin_decode","invoked":false,
             "profile_resolution":{"mode":"runtime_signatures","rules_sha256":"rules","ready":false,
             "cache_hit":false,"timestamp":19088743,"image_size":33554432,"elapsed_ms":17,"stage":"locator:skin_decode"}}
            """);
        var failure = NativeSkinCoreTransport.ReplyFailure(reply, 0);
        Assert.Equal("profile_signature_missing", failure.Code);
        Assert.False(failure.OutcomeUnknown);
        var details = Assert.IsType<SkinCoreFailureDetails>(failure.Details);
        Assert.Equal("locator:skin_decode", details.Stage);
        Assert.Equal("runtime_signatures", details.ProfileResolution!.Mode);
        Assert.Equal("rules", details.ProfileResolution.RulesSha256);
        Assert.Equal(17, details.ProfileResolution.ElapsedMs);
    }
}
