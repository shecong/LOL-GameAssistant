using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Infrastructure.Coaching;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class RecommendationCoordinatorTests
{
    private const string Advice = """
        {"recommendations":[{"opportunityId":"fight.plan","priority":"attention","title":"先拉开盖伦的进场距离","steps":["安妮先站在队友侧后方，留住控制应对盖伦进场。","等盖伦交出加速且队友能跟进，再交控制反打。"],"conditions":"确认队友能跟进且自己控制技能可用时","why":"安妮需要靠控制创造输出时间，先拉开盖伦距离能避免被近身沉默。","expectedOutcome":"保住控制并争取一轮安全输出","fallback":"队友无法跟进就继续后撤，不追击盖伦。","invalidWhen":"盖伦撤离或自身控制技能已交时停止","actionKey":"teamfight:position","factIds":["champion","allies","enemies","items"]}]}
        """;
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(int seconds) => Now = Now.AddSeconds(seconds);
    }
    private sealed class Contexts : IAiCoachingService
    {
        public AiGameContext Value = new() { Phase = "InProgress", MyChampionId = 1, MyChampion = "安妮", HasLiveClientData = true, CurrentItems = ["鞋子"], EnemyChampions = ["盖伦"], AlliedChampions = ["安妮", "艾希"] };
        public Task<AiGameContext> CollectContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value);
    }
    private sealed class Provider : IAiRecommendationProvider
    {
        public int Calls;
        public Func<AiGameContext, CancellationToken, Task<AiRecommendationResult>>? Handler;
        public Task<AiRecommendationResult> CreateAsync(CloudAiSettings settings, AiGameContext context, CancellationToken cancellationToken = default)
        { Interlocked.Increment(ref Calls); return Handler?.Invoke(context, cancellationToken) ?? Task.FromResult(new AiRecommendationResult(true, "", Advice, context, null)); }
        public Task<string> TestAsync(CloudAiSettings settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListModelsAsync(CloudAiSettings settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private static AssistantSettings Settings(int budget = 30, bool automatic = true) => new()
    { Ai = new() { Model = "test", EncryptedApiKey = "test", MatchRequestBudget = budget, DynamicRefreshEnabled = automatic } };

    [Fact]
    public async Task GoldAndTimeOnlyRefreshLocallyEquipmentDebouncesAndOldAdviceDisappearsImmediately()
    {
        var clock = new Clock(); var contexts = new Contexts(); var provider = new Provider();
        using var coordinator = new RecommendationCoordinator(contexts, provider, clock);
        coordinator.UpdateSettings(Settings());
        await coordinator.RefreshAsync(force: true);
        Assert.Single(coordinator.Current.Recommendations);
        contexts.Value = contexts.Value with { CurrentGold = 700, GameTimeSeconds = 61 };
        clock.Advance(5); await coordinator.RefreshAsync();
        Assert.Equal(1, provider.Calls);
        contexts.Value = contexts.Value with { CurrentItems = ["长剑"] };
        await coordinator.RefreshAsync();
        Assert.Empty(coordinator.Current.Recommendations);
        clock.Advance(30); await coordinator.RefreshAsync();
        Assert.Equal(2, provider.Calls);
        Assert.Single(coordinator.Current.Recommendations);
    }

    [Fact]
    public async Task LateResponseCannotPublishAfterHeroChangeAndRepeatedClicksShareActiveRequest()
    {
        var contexts = new Contexts(); var provider = new Provider(); var clock = new Clock();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = new TaskCompletionSource<AiRecommendationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken oldToken = default;
        provider.Handler = (_, token) => { oldToken = token; started.TrySetResult(); return result.Task; };
        using var coordinator = new RecommendationCoordinator(contexts, provider, clock);
        coordinator.UpdateSettings(Settings());
        var first = coordinator.RefreshAsync(true);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.RefreshAsync(true); await coordinator.RefreshAsync(true);
        Assert.Equal(1, provider.Calls);
        contexts.Value = contexts.Value with { MyChampionId = 2, MyChampion = "奥拉夫" };
        await coordinator.RefreshAsync();
        Assert.True(oldToken.IsCancellationRequested);
        result.SetResult(new(true, "", Advice, contexts.Value, null));
        await first;
        Assert.Empty(coordinator.Current.Recommendations);
        Assert.Null(coordinator.Current.AnalyzedAt);
        provider.Handler = null; clock.Advance(30); await coordinator.RefreshAsync();
        Assert.Equal(2, provider.Calls);
        Assert.Equal(2, coordinator.Current.Context.MyChampionId);
    }

    [Fact]
    public async Task BudgetCountsRequestsAndRaisingItResumesAnalysis()
    {
        var clock = new Clock(); var provider = new Provider(); var contexts = new Contexts();
        using var coordinator = new RecommendationCoordinator(contexts, provider, clock);
        coordinator.UpdateSettings(Settings(1)); await coordinator.RefreshAsync(true);
        clock.Advance(10); await coordinator.RefreshAsync(true);
        Assert.Equal(RecommendationStatus.BudgetPaused, coordinator.Current.Status);
        Assert.Equal(1, provider.Calls);
        coordinator.UpdateSettings(Settings(2)); await coordinator.RefreshAsync(true);
        Assert.Equal(2, provider.Calls);
        Assert.Equal(2, coordinator.Current.RequestCount);
    }

    [Fact]
    public async Task DisablingAutoStillExpiresAdviceAndFeedbackIsLocal()
    {
        var clock = new Clock(); var provider = new Provider();
        using var coordinator = new RecommendationCoordinator(new Contexts(), provider, clock);
        coordinator.UpdateSettings(Settings(automatic: false)); await coordinator.RefreshAsync(true);
        var card = Assert.Single(coordinator.Current.Recommendations);
        coordinator.RecordFeedback(card.Id, "有帮助");
        Assert.Equal("有帮助", Assert.Single(coordinator.Current.History).Feedback);
        clock.Advance(121); await coordinator.RefreshAsync();
        Assert.Empty(coordinator.Current.Recommendations);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task MissingLiveDataAndMalformedResultsNeverBecomeReady()
    {
        var clock = new Clock(); var provider = new Provider(); var contexts = new Contexts();
        using var coordinator = new RecommendationCoordinator(contexts, provider, clock);
        coordinator.UpdateSettings(Settings());
        contexts.Value = contexts.Value with { HasLiveClientData = false };
        await coordinator.RefreshAsync(true);
        Assert.Equal(0, provider.Calls);
        Assert.Equal(RecommendationStatus.DataUnavailable, coordinator.Current.Status);
        contexts.Value = contexts.Value with { HasLiveClientData = true };
        provider.Handler = (c, _) => Task.FromResult(new AiRecommendationResult(true, "", "{broken", c, null));
        await coordinator.RefreshAsync(true);
        Assert.Equal(RecommendationStatus.FormatError, coordinator.Current.Status);
        Assert.Empty(coordinator.Current.Recommendations);
    }

    [Fact]
    public async Task ConfirmedMatchupIsValidatedAndSessionEndKeepsHistoryUntilNextGame()
    {
        var clock = new Clock(); var provider = new Provider(); var contexts = new Contexts();
        using var coordinator = new RecommendationCoordinator(contexts, provider, clock);
        coordinator.UpdateSettings(Settings()); await coordinator.RefreshAsync(true);
        coordinator.ConfirmMatchup("不存在的英雄");
        Assert.Equal("", coordinator.Current.Context.MatchupChampion);
        coordinator.ConfirmMatchup("盖伦");
        Assert.Equal("盖伦", coordinator.Current.Context.MatchupChampion);
        Assert.Empty(coordinator.Current.Recommendations);
        contexts.Value = contexts.Value with { Phase = "EndOfGame" };
        await coordinator.RefreshAsync();
        Assert.Single(coordinator.Current.History);
        contexts.Value = contexts.Value with { Phase = "ChampSelect" };
        await coordinator.RefreshAsync();
        Assert.Empty(coordinator.Current.History);
        Assert.Equal(0, coordinator.Current.RequestCount);
        Assert.Equal("", coordinator.Current.Context.MatchupChampion);
    }

    [Fact]
    public void NewSettingsRoundTripThroughLegacyStorageAndOldSettingsGetDefaults()
    {
        var original = Settings(42);
        original.Ai.MinimumRequestSeconds = 45;
        original.Ai.StructuredOutputEnabled = false;
        var stored = LOL_GameAssistant.Infrastructure.Settings.LegacySettingsMapper.ToLegacy(original);
        var restored = LOL_GameAssistant.Infrastructure.Settings.LegacySettingsMapper.ToDomain(stored);
        Assert.Equal(42, restored.Ai.MatchRequestBudget);
        Assert.Equal(45, restored.Ai.MinimumRequestSeconds);
        Assert.False(restored.Ai.StructuredOutputEnabled);
        var legacy = Newtonsoft.Json.JsonConvert.DeserializeObject<LOL_GameAssistant.Entity.AiSettings>("{\"dynamicRefreshSeconds\":30}")!;
        Assert.Equal(30, legacy.DynamicRefreshSeconds);
        Assert.Equal(30, legacy.MatchRequestBudget);
        Assert.True(legacy.StructuredOutputEnabled);
    }

    [Fact]
    public async Task EmptyAnalysisWaitsForFallbackIntervalAndKeepsStillValidPlan()
    {
        var clock = new Clock(); var contexts = new Contexts(); var provider = new Provider();
        using var coordinator = new RecommendationCoordinator(contexts, provider, clock);
        coordinator.UpdateSettings(Settings()); await coordinator.RefreshAsync(true);
        var first = Assert.Single(coordinator.Current.Recommendations);
        provider.Handler = (c, _) => Task.FromResult(new AiRecommendationResult(true, "", "{\"recommendations\":[]}", c, null));
        clock.Advance(10); await coordinator.RefreshAsync(true);
        Assert.Equal(RecommendationStatus.NoActionNeeded, coordinator.Current.Status);
        Assert.Equal(first.Id, Assert.Single(coordinator.Current.Recommendations).Id);
        Assert.Single(coordinator.Current.History);
        clock.Advance(35); await coordinator.RefreshAsync();
        Assert.Equal(2, provider.Calls);
        clock.Advance(60); await coordinator.RefreshAsync();
        Assert.Equal(3, provider.Calls);
        Assert.Empty(coordinator.Current.Recommendations);
    }

    [Fact]
    public async Task LateEquipmentDependentAdviceIsDiscardedWithoutCancellingUnrelatedAnalysis()
    {
        var clock = new Clock(); var contexts = new Contexts(); var provider = new Provider();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<AiRecommendationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;
        provider.Handler = (_, ct) => { token = ct; started.TrySetResult(); return response.Task; };
        using var coordinator = new RecommendationCoordinator(contexts, provider, clock);
        coordinator.UpdateSettings(Settings());
        var task = coordinator.RefreshAsync(true); await started.Task;
        contexts.Value = contexts.Value with { CurrentItems = ["法师之靴"] };
        await coordinator.RefreshAsync();
        Assert.False(token.IsCancellationRequested);
        response.SetResult(new(true, "", Advice, contexts.Value, null));
        await task;
        Assert.Empty(coordinator.Current.Recommendations);
        Assert.Equal(RecommendationStatus.Stale, coordinator.Current.Status);
    }

    [Fact]
    public async Task ImmediateSurvivalAdviceDoesNotWaitForCloudAndClearsOnRecovery()
    {
        var clock = new Clock(); var contexts = new Contexts(); var provider = new Provider();
        contexts.Value = contexts.Value with { Telemetry = new()
        {
            Health = 100, MaxHealth = 1000, Gold = 0,
            Players = [new() { Key = "p0", IsSelf = true, IsAlly = true, IsDead = false, Champion = "安妮", HasItems = true }]
        } };
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<AiRecommendationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.Handler = (_, _) => { started.TrySetResult(); return response.Task; };
        using var coordinator = new RecommendationCoordinator(contexts, provider, clock);
        coordinator.UpdateSettings(Settings());
        var task = coordinator.RefreshAsync(true); await started.Task;
        Assert.Equal(RecommendationSource.LocalObservation, Assert.Single(coordinator.Current.Recommendations).Source);
        contexts.Value = contexts.Value with { Telemetry = contexts.Value.Telemetry! with { Health = 900 } };
        await coordinator.RefreshAsync();
        Assert.Empty(coordinator.Current.Recommendations);
        response.SetResult(new(true, "", "{\"recommendations\":[]}", contexts.Value, null));
        await task;
    }

    [Fact]
    public async Task RepeatedFailuresBackOffAndNegativeFeedbackRemovesCard()
    {
        var clock = new Clock(); var contexts = new Contexts(); var provider = new Provider();
        using var coordinator = new RecommendationCoordinator(contexts, provider, clock);
        coordinator.UpdateSettings(Settings()); await coordinator.RefreshAsync(true);
        coordinator.RecordFeedback(Assert.Single(coordinator.Current.Recommendations).Id, "不适用");
        Assert.Empty(coordinator.Current.Recommendations);
        provider.Handler = (c, _) => Task.FromResult(AiRecommendationResult.Unavailable("", c, "offline"));
        clock.Advance(30); await coordinator.RefreshAsync();
        Assert.Equal(2, provider.Calls);
        clock.Advance(30); await coordinator.RefreshAsync();
        Assert.Equal(3, provider.Calls);
        clock.Advance(30); await coordinator.RefreshAsync();
        Assert.Equal(3, provider.Calls);
        Assert.True(coordinator.Current.NextRefreshAt > clock.Now);
    }
}
