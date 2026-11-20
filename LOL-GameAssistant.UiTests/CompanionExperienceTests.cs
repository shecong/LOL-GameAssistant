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
    [Fact]
    public async Task StorePersistsPerPlayerAndMergesConcurrentChangesAcrossInstances()
    {
        using var folder = new Folder(); var first = new CompanionExperienceStore(folder.Path); var second = new CompanionExperienceStore(folder.Path);
        await first.UpdateAsync("one/../", p => p.PlayStyle = "认真上分");
        string file = Assert.Single(Directory.GetFiles(folder.Path));
        await File.WriteAllTextAsync(file, """
            {"Goal":{"Metric":1,"Target":5},"PatchVersion":"old","PatchAssets":{},"PlayStyle":"认真上分"}
            """);
        Assert.Equal("认真上分", (await first.LoadAsync("one/../")).PlayStyle);
        await Task.WhenAll(first.UpdateAsync("one/../", p => p.Voice = true),
            second.UpdateAsync("one/../", p => p.FavoriteTeammates["friend"] = "队友#123"));
        var value = await second.LoadAsync("one/../");
        Assert.True(value.Voice);
        Assert.Equal("认真上分", value.PlayStyle);
        Assert.Single(value.FavoriteTeammates);
        Assert.Empty((await first.LoadAsync("two")).FavoriteTeammates);
        Assert.DoesNotContain("PatchVersion", await File.ReadAllTextAsync(file));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            first.UpdateAsync("one/../", p => p.Voice = false, cancelled.Token));
        Assert.True((await first.LoadAsync("one/../")).Voice);
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
        Assert.Equal(preset != AssistantPreset.Quiet, settings.AutoApplyRuneBuild);
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
}
