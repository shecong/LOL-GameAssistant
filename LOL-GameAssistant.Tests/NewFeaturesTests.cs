using LOL_GameAssistant.Domain.Builds;
using LOL_GameAssistant.Domain.ChampionSelect;
using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.LeagueClient;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class NewFeaturesTests
{
    [Fact]
    public void PersonalPresets_ChooseHighestPriorityForExactChampionModeAndRole()
    {
        PersonalRunePreset Make(int champion, string mode, string role, int priority) => new()
        {
            ChampionId = champion, Mode = mode, Position = role, Priority = priority, AutoApply = true,
            PrimaryStyleId = 8000, SubStyleId = 8100,
            RunePerkIds = [8005, 9111, 9104, 8014, 8139, 8105], SummonerSpellIds = [4, 14]
        };
        var wrongChampion = Make(22, "ranked", "mid", 99);
        var wrongRole = Make(1, "ranked", "top", 90);
        var preferred = Make(1, "ranked", "mid", 10);
        var lower = Make(1, "ranked", "mid", 1);
        var result = PersonalRunePresetResolver.Find(
            [wrongChampion, wrongRole, lower, preferred], 1, "ranked", "MIDDLE");
        Assert.Same(preferred, result);
        Assert.Null(PersonalRunePresetResolver.Find([wrongChampion], 1, "ranked", "MIDDLE"));
    }

    [Fact]
    public void AramBench_OnlySwapsToAvailableHigherPriorityChampion()
    {
        Assert.Equal(22, AramBenchPriorityResolver.Choose(99, [44, 22], [11, 22, 99]));
        Assert.Null(AramBenchPriorityResolver.Choose(11, [22, 99], [11, 22, 99]));
        Assert.Null(AramBenchPriorityResolver.Choose(99, [44], [11, 22, 99]));
    }

    [Fact]
    public void AugmentOcr_MatchesClosedVocabularyAndKeepsOnlyThreeOptions()
    {
        var names = new Dictionary<int, string>
        {
            [1] = "珠光护手", [2] = "巨人杀手", [3] = "神圣祝福", [4] = "不灭之握"
        };
        var ids = AugmentNameMatcher.Match("珠光护手\n巨人杀手\n神圣祝福\n不灭之握", names);
        Assert.Equal([1, 2, 3], ids);
    }

    [Fact]
    public void AugmentOcr_UsesMayhemIdOnceWhenArenaHasTheSameName()
    {
        (int Id, string Name)[] names = [(36, "火上浇油"), (1036, "火上浇油"),
            (2080, "冰雪爆裂")];
        Assert.Equal([1036, 2080], AugmentNameMatcher.Match("火上浇油\n冰雪爆裂", names));
    }

    [Fact]
    public void AugmentOcr_JoinsTitleSplitByPunctuation()
    {
        (int Id, string Name)[] names = [(1319, "升级：献祭")];
        Assert.Equal([1319], AugmentNameMatcher.Match("升级：\n献祭", names));
    }

    [Fact]
    public void MayhemInProgress_PreservesLcuQueueWhenLiveClientDataIsUnavailable()
    {
        var session = new ActiveGameSnapshot { GameMode = "KIWI", QueueId = 3270 };
        var result = LiveModeContextResolver.Resolve(session, null);
        Assert.Equal("KIWI", result.GameMode);
        Assert.Equal(3270, result.QueueId);
        Assert.Equal("海克斯大乱斗", result.DisplayMode);
    }
}
