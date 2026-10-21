using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Infrastructure.Ai;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class ActionableCoachingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);
    private static AiGameContext Live => new()
    {
        Phase = "InProgress", GameMode = "CLASSIC", MyChampionId = 1, MyChampion = "安妮", MyRole = "MIDDLE",
        GameTimeSeconds = 650, CurrentGold = 1500, HasLiveClientData = true, MatchupChampion = "盖伦",
        CurrentItems = ["鞋子"], AlliedChampions = ["安妮", "艾希"], EnemyChampions = ["盖伦"],
        Telemetry = new()
        {
            Gold = 1500, Level = 6, Health = 200, MaxHealth = 1000, ResourceType = "MANA", Resource = 100, MaxResource = 500,
            Abilities = [new("Q", "碎裂之火", 3), new("R", "提伯斯之怒", 1)],
            Players = [new() { Key = "p0", Champion = "安妮", IsSelf = true, IsAlly = true, IsDead = false, HasItems = true },
                new() { Key = "p1", Champion = "盖伦", IsAlly = false, IsDead = false, HasItems = true }]
        }
    };
    private const string Survival = """
        {"recommendations":[{"opportunityId":"survival.health","priority":"important","title":"先退出近身换血范围","actionKey":"survival:disengage","factIds":["champion","self.health"],"steps":["安妮先退到己方防御塔或队友后方，停止上前普攻换血。","留住控制技能保护撤退路线，确认安全后再处理补给。"],"conditions":"确认撤退路线可见且没有敌人正在包夹时","why":"当前生命只剩两成，安妮继续近身换血难以承受对方反击，应先保留控制换取退路。","expectedOutcome":"避免再承受一轮换血并保住回补机会","fallback":"退路被堵时先靠近可支援队友，保留控制阻止追击。","invalidWhen":"生命恢复到安全区间或退路发生变化时停止"}]}
        """;

    [Fact]
    public void LowHealthGeneratesSurvivalButNotAggressiveLaneOrTeamfightPlans()
    {
        var frame = CoachingDecisionFrame.Create(Live);
        Assert.Contains(frame.Opportunities, o => o.Id == "survival.health");
        Assert.Contains(frame.Opportunities, o => o.Id == "resource.mana");
        Assert.DoesNotContain(frame.Opportunities, o => o.Id is "fight.plan" or "lane.matchup");
        var card = Assert.Single(CloudRecommendationParser.Parse(Survival, Now, Live, true));
        Assert.False(card.IsFormatError);
        Assert.Equal(2, card.Steps.Count);
        Assert.Contains("200/1000", card.Evidence);
        Assert.Equal(Now.AddSeconds(25), card.ExpiresAt);
    }

    [Fact]
    public void DeathOnlyAllowsRespawnPreparationAndMissingGoldDoesNotBecomeZero()
    {
        var dead = Live with { Telemetry = Live.Telemetry! with { Players = [Live.Telemetry!.Self! with { IsDead = true }] } };
        Assert.Equal("death.reset", Assert.Single(CoachingDecisionFrame.Create(dead).Opportunities).Id);
        var missing = dead with { Telemetry = dead.Telemetry! with { Gold = null } };
        Assert.DoesNotContain(CoachingDecisionFrame.Create(missing).Facts, f => f.Id == "gold");
        Assert.Empty(CoachingDecisionFrame.Create(missing).Opportunities);
    }

    [Fact]
    public void EvidenceSpecificValidityPreservesAdviceAcrossUnrelatedChangesAndExpiresOnRecovery()
    {
        var card = Assert.Single(CloudRecommendationParser.Parse(Survival, Now, Live, true));
        Assert.True(RecommendationPolicy.IsValid(card, Live with { CurrentGold = 1900, CurrentItems = ["法师之靴"] }, Now.AddSeconds(5)));
        Assert.False(RecommendationPolicy.IsValid(card, Live with { Telemetry = Live.Telemetry! with { Health = 900 } }, Now.AddSeconds(5)));
        Assert.False(RecommendationPolicy.IsValid(card, Live, Now.AddSeconds(26)));
    }

    [Theory]
    [InlineData("\"self.health\"", "\"enemy.cooldowns\"")]
    [InlineData("survival:disengage", "teamfight:engage")]
    [InlineData("留住控制技能保护撤退路线，确认安全后再处理补给。", "注意走位")]
    [InlineData("退路被堵时先靠近可支援队友，保留控制阻止追击。", "视情况")]
    [InlineData("安妮先退到己方防御塔或队友后方，停止上前普攻换血。", "安妮注意走位，配合队友并提高容错。")]
    [InlineData("\"important\"", "\"urgent\"")]
    [InlineData("\"self.health\"]", "\"self.health\",42]")]
    [InlineData("确认安全后再处理补给。\"]", "确认安全后再处理补给。\",false]")]
    public void MissingFactsWrongActionsAndEmptyPlatitudesAreRejected(string from, string to)
        => Assert.True(Assert.Single(CloudRecommendationParser.Parse(Survival.Replace(from, to), Now, Live, true)).IsFormatError);

    [Fact]
    public void ValidEmptyAnalysisIsNotAnErrorButLegacyGenericJsonIsRejectedInProduction()
    {
        Assert.Empty(CloudRecommendationParser.Parse("{\"recommendations\":[]}", Now, Live, true));
        Assert.True(Assert.Single(CloudRecommendationParser.Parse("""
            {"recommendations":[{"title":"提高容错","body":"可以考虑提高生存能力。","conditions":"需要提高容错时","actionKey":"build:survival","evidenceFields":["champion"]}]}
            """, Now, Live, true)).IsFormatError);
    }

    [Fact]
    public void AramAndUnknownModesDoNotReceiveRiftRecallOrLanePlans()
    {
        foreach (string mode in new[] { "ARAM", "CHERRY", "UNKNOWN" })
        {
            var c = Live with { GameMode = mode, Mode = mode == "ARAM" ? "深渊大乱斗" : "未知玩法" };
            Assert.DoesNotContain(CoachingDecisionFrame.Create(c).Opportunities, o => o.Id is "economy.spend" or "lane.matchup" or "economy.farm");
        }
    }

    [Fact]
    public void FarmAdviceRequiresObservedWindowAndExcludesSupportsAndDeaths()
    {
        var farm = Live with { FarmWindow = new(100, 2, 0) };
        Assert.Contains(CoachingDecisionFrame.Create(farm).Opportunities, o => o.Id == "economy.farm");
        Assert.DoesNotContain(CoachingDecisionFrame.Create(farm with { MyRole = "UTILITY" }).Opportunities, o => o.Id == "economy.farm");
        Assert.DoesNotContain(CoachingDecisionFrame.Create(farm with { FarmWindow = new(100, 2, 1) }).Opportunities, o => o.Id == "economy.farm");
    }

    [Fact]
    public void AugmentNamesAloneCannotJustifyInventedEffects()
    {
        var c = Live with { ConfirmedAugmentOptions = ["测试增幅"] };
        Assert.DoesNotContain(CoachingDecisionFrame.Create(c).Opportunities, o => o.Id == "augment.choice");
        Assert.Contains(CoachingDecisionFrame.Create(c with { ConfirmedAugmentDescriptions = ["测试增幅：提供生命值"] }).Opportunities, o => o.Id == "augment.choice");
    }

    [Theory]
    [InlineData("ARAM")]
    [InlineData("KIWI")]
    [InlineData("CHERRY")]
    [InlineData("UNKNOWN")]
    public void RecallInstructionsCannotLeakThroughSurvivalOrFallbackInOtherModes(string mode)
    {
        // 展示名称尚未同步时，也必须使用原始模式保护规则。
        var c = Live with { GameMode = mode, Mode = "峡谷对局" };
        var local = CoachingImmediateAdvice.Create(c, Now)!;
        Assert.DoesNotContain("回城", local.Body);
        string badStep = Survival.Replace("留住控制技能保护撤退路线，确认安全后再处理补给。", "安妮先找安全的位置回城，然后购买防御组件。");
        string badFallback = Survival.Replace("退路被堵时先靠近可支援队友，保留控制阻止追击。", "没有安全的换血窗口时，安妮先回城再购买装备。");
        Assert.True(Assert.Single(CloudRecommendationParser.Parse(badStep, Now, c, true)).IsFormatError);
        Assert.True(Assert.Single(CloudRecommendationParser.Parse(badFallback, Now, c, true)).IsFormatError);
    }

    [Fact]
    public void LocalSurvivalRequiresKnownAliveStateAndExpiresOnDeath()
    {
        var card = CoachingImmediateAdvice.Create(Live, Now)!;
        Assert.Contains("回城", card.Body);
        foreach (bool? dead in new bool?[] { true, null })
        {
            var c = Live with { Telemetry = Live.Telemetry! with { Players = [Live.Telemetry!.Self! with { IsDead = dead }] } };
            Assert.Null(CoachingImmediateAdvice.Create(c, Now));
            Assert.False(RecommendationPolicy.IsValid(card, c, Now.AddSeconds(1)));
        }
    }
}
