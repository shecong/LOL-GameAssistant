using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Infrastructure.Ai;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class RecommendationPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);
    private static AiGameContext Context => new() { Phase = "InProgress", MyChampionId = 1, MyChampion = "安妮", HasLiveClientData = true, CurrentItems = ["鞋子"], EnemyChampions = ["盖伦"], AlliedChampions = ["安妮"] };
    private const string Card = """
        {"recommendations":[{"opportunityId":"fight.plan","priority":"attention","title":"先拉开盖伦的进场距离","steps":["安妮先站在队友侧后方，留住控制应对盖伦进场。","等盖伦交出加速且队友能跟进，再交控制反打。"],"conditions":"确认队友能跟进且自己控制技能可用时","why":"安妮需要靠控制创造输出时间，先拉开盖伦距离能避免被近身沉默。","expectedOutcome":"保住控制并争取一轮安全输出","fallback":"队友无法跟进就继续后撤，不追击盖伦。","invalidWhen":"盖伦撤离或自身控制技能已交时停止","actionKey":"teamfight:position","factIds":["champion","allies","enemies","items"]}]}
        """;

    [Fact]
    public void GoldAndTimeDoNotChangeDecisionVersionButAlliesAndEquipmentDo()
    {
        Assert.Equal(RecommendationPolicy.Version(Context), RecommendationPolicy.Version(Context with { CurrentGold = 900, GameTimeSeconds = 61 }));
        Assert.NotEqual(RecommendationPolicy.Version(Context), RecommendationPolicy.Version(Context with { AlliedChampions = ["安妮", "盖伦"] }));
        Assert.NotEqual(RecommendationPolicy.Version(Context), RecommendationPolicy.Version(Context with { CurrentItems = ["鞋子", "长剑"] }));
    }

    [Fact]
    public void CardsExpireAndAreInvalidatedByEquipmentAndHeroChanges()
    {
        var card = Assert.Single(CloudRecommendationParser.Parse(Card, Now, Context, true));
        Assert.False(card.IsFormatError);
        Assert.True(RecommendationPolicy.IsValid(card, Context, Now.AddSeconds(30)));
        Assert.False(RecommendationPolicy.IsValid(card, Context, Now.AddMinutes(2)));
        Assert.False(RecommendationPolicy.IsValid(card, Context with { CurrentItems = ["长剑"] }, Now));
        Assert.False(RecommendationPolicy.IsValid(card, Context with { MyChampionId = 2 }, Now));
        Assert.Contains("鞋子", card.Evidence);
    }

    [Theory]
    [InlineData("\"items\"", "\"matchup\"")]
    [InlineData("\"items\"", "\"augments\"")]
    [InlineData("\"items\"", "\"hidden.enemyGold\"")]
    [InlineData("teamfight:position", "任意措辞")]
    public void InvalidOrUnavailableEvidenceIsRejected(string before, string after)
    {
        Assert.True(Assert.Single(CloudRecommendationParser.Parse(Card.Replace(before, after), Now, Context, true)).IsFormatError);
    }

    [Fact]
    public void StrictModeRejectsTruncatedJsonAndTextWhileCompatibilityKeepsText()
    {
        Assert.True(Assert.Single(CloudRecommendationParser.Parse(Card[..^3], Now, Context, true)).IsFormatError);
        Assert.True(Assert.Single(CloudRecommendationParser.Parse("可考虑提高容错", Now, Context, true)).IsFormatError);
        Assert.False(Assert.Single(CloudRecommendationParser.Parse("可考虑提高容错", Now, Context, false)).IsFormatError);
    }
}
