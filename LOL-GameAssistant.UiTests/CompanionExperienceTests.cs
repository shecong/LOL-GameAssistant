using System.Net;
using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Infrastructure.Files;
using LOL_GameAssistant.Infrastructure.GameData;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

public sealed class CompanionExperienceTests
{
    private sealed class Folder : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "companion-experience-tests", Guid.NewGuid().ToString("N"));
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
    private static GrowthMatch Match(long id, double? cs = 5, int hero = 1, string mode = "排位", int? deaths = 5) =>
        new(id, 1000 + id * 1000, hero, mode, 1200, true, 2, cs, null, null) { Deaths = deaths };

    [Fact]
    public void ReviewRequiresSameHeroModeAndThreeKnownOlderSamples()
    {
        var current = Match(10, 3, deaths: 2);
        var review = CompanionExperience.Review(current, [Match(1), Match(2), Match(3), Match(11, 100), Match(4, 100, 2), Match(5, 100, mode: "大乱斗")]);
        Assert.Equal(3, review.BaselineGames);
        Assert.Contains("死亡次数", review.Highlight);
        Assert.Contains("补刀/分", review.Improvement);
        Assert.Contains("本局 3", review.Improvement);
        var insufficient = CompanionExperience.Review(current, [Match(1), Match(2), Match(3, null, deaths: null)]);
        Assert.Contains("足够样本", insufficient.Improvement);
        Assert.DoesNotContain("死亡原因", review.Improvement);
    }

    [Fact]
    public void GoalCountsOnlyNewMatchingGamesAndMissingDataDoesNotConsumeQuota()
    {
        var goal = new PracticeGoal(PracticeMetric.Cs, 5, "排位", 1, DateTimeOffset.FromUnixTimeMilliseconds(3000), 2);
        var result = CompanionExperience.Progress([Match(1), Match(2, null), Match(3, 6), Match(4, 1, 2), Match(5, 1, mode: "大乱斗"), Match(6, 4), Match(6, 4), Match(7, 100)], goal);
        Assert.Equal(new PracticeProgress(2, 1, 1, 2), result);
        Assert.Equal(1, CompanionExperience.Progress([Match(3, deaths: 2)], goal with { Metric = PracticeMetric.Deaths, Target = 3 }).Passed);
    }

    [Fact]
    public async Task StorePersistsPerPlayerAndMergesConcurrentChangesAcrossInstances()
    {
        using var folder = new Folder(); var first = new CompanionExperienceStore(folder.Path); var second = new CompanionExperienceStore(folder.Path);
        var goal = new PracticeGoal(PracticeMetric.Cs, 5, "排位", null, DateTimeOffset.UtcNow);
        await Task.WhenAll(first.UpdateAsync("one/../", p => p.Goal = goal), second.UpdateAsync("one/../", p => p.FavoriteTeammates["friend"] = "队友#123"));
        var value = await second.LoadAsync("one/../"); Assert.Equal(goal, value.Goal); Assert.Single(value.FavoriteTeammates);
        Assert.Null((await first.LoadAsync("two")).Goal);
        Assert.Single(Directory.GetFiles(folder.Path));
        await Assert.ThrowsAsync<ArgumentException>(() => first.UpdateAsync("one/../", p => p.Goal = goal with { Mode = "全部" }));
        Assert.Equal(goal, (await first.LoadAsync("one/../")).Goal);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.UpdateAsync("one/../", p => p.Goal = null, cancelled.Token));
        Assert.Equal(goal, (await first.LoadAsync("one/../")).Goal);
    }

    [Theory]
    [InlineData(AssistantPreset.Ranked)]
    [InlineData(AssistantPreset.Aram)]
    [InlineData(AssistantPreset.Quiet)]
    public void PresetsKeepSecretsPersonalSelectionsAndNeverEnableAutoLockOrChat(AssistantPreset preset)
    {
        var settings = new AssistantSettings { GameClientPath = "D:/LOL", SkinCoreEnabled = true, PickChampions = ["安妮"], AutoPick = true, AutoMatch = true,
            Ai = new() { EncryptedApiKey = "protected", Model = "my-model", RecommendationEnabled = true } };
        AssistantPresetPolicy.Apply(settings, preset);
        Assert.Equal("protected", settings.Ai.EncryptedApiKey); Assert.Equal("my-model", settings.Ai.Model);
        Assert.Equal("D:/LOL", settings.GameClientPath); Assert.True(settings.SkinCoreEnabled); Assert.Equal("安妮", Assert.Single(settings.PickChampions));
        Assert.False(settings.AutoPick); Assert.False(settings.AutoMatch); Assert.False(settings.GameKdaAnnouncementEnabled); Assert.False(settings.Ai.RecommendationEnabled);
        Assert.Equal(preset == AssistantPreset.Aram, settings.MayhemOverlayEnabled);
    }

    [Fact]
    public async Task PatchComparisonUsesOnlyTrackedAssetsAndNeverAcknowledgesAutomatically()
    {
        int version = 1; var urls = new List<string>();
        using var http = new HttpClient(new Handler(uri =>
        {
            urls.Add(uri.AbsolutePath);
            if (uri.AbsolutePath.EndsWith("versions.json")) return $"[\"16.{version}.1\"]";
            if (uri.AbsolutePath.EndsWith("champion.json")) return """{"data":{"Annie":{"key":"1","id":"Annie"},"Olaf":{"key":"2","id":"Olaf"}}}""";
            if (uri.AbsolutePath.EndsWith("champion/Annie.json")) return """{"data":{"Annie":{"name":"安妮","stats":{"hp":VALUE},"spells":[],"passive":{"description":"被动"}}}}""".Replace("VALUE", (version * 100).ToString());
            if (uri.AbsolutePath.EndsWith("item.json")) return """{"data":{"1001":{"name":"鞋子","description":"移动速度","gold":{"total":VALUE}}}}""".Replace("VALUE", (version * 300).ToString());
            throw new Exception("Unexpected request " + uri);
        }));
        var service = new DataDragonPatchWatchService(http); var previous = new PlayerExperience();
        var first = await service.CheckAsync(previous, [1], [1001]);
        Assert.Empty(previous.PatchAssets); Assert.Equal(2, first.Assets.Count);
        previous.PatchAssets = new(first.Assets); previous.PatchVersion = first.CurrentVersion; version = 2;
        var next = await service.CheckAsync(previous, [1], [1001]);
        Assert.Equal("16.1.1", previous.PatchVersion); Assert.Equal("16.2.1", next.CurrentVersion);
        Assert.Contains(next.Changes, c => c.Contains("安妮") && c.Contains("生命值") && c.Contains("100 → 200")); Assert.Contains(next.Changes, c => c.Contains("鞋子") && c.Contains("价格"));
        Assert.DoesNotContain(urls, url => url.EndsWith("Olaf.json"));
    }

    [Fact]
    public async Task BuildExplanationSeparatesUnknownVersionIndependentStatsAndItemEffects()
    {
        var option = new OpggBuildOption(1, [], [1001], [3001], 1, 2, [1, 2], 200, 110);
        var choices = new OpggBuildChoices(true, "", "安妮", "中路", [option, option with { Order = 2, CoreItemIds = [3001], Matches = 10, Wins = 9 }]);
        var assets = Service<IGameAssetService>((m, a) => Task.FromResult<string?>(m.Name == "GetItemDescriptionAsync" ? "<mainText>移动速度</mainText>" : "装备" + a[0]));
        string text = await BuildComparisonForm.DescribeAsync(choices, assets, default);
        Assert.Contains("来源未提供", text); Assert.Contains("不代表整套组合的联合胜率", text); Assert.Contains("装备1001：移动速度", text);
        Assert.DoesNotContain("<mainText>", text); Assert.Contains("≥100 场样本中胜率最高", text);
    }

    [Fact]
    public void RecruitmentTagsRemainCompatibleWithExistingNoteLimit()
    {
        string note = RecruitmentPreferences.Compose("认真上分", true, "周末20:00", "友好交流");
        Assert.Equal("认真上分", RecruitmentPreferences.Style(note)); Assert.Contains("愿意语音", note);
        Assert.Null(RecruitmentPreferences.Style("旧版自由文本"));
        Assert.Throws<ArgumentException>(() => RecruitmentPreferences.Compose("无效", false, "", ""));
        Assert.Throws<ArgumentException>(() => RecruitmentPreferences.Compose("认真上分", true, "", new string('字', 120)));
    }

    private sealed class Handler(Func<Uri, string> read) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(read(request.RequestUri!)) });
    }
}
