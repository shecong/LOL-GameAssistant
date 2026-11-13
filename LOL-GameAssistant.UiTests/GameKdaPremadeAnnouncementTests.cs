using System.Reflection;
using LOL_GameAssistant.Application.ChampionSelect;
using LOL_GameAssistant.Application.Lobby;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Domain.ChampionSelect;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Domain.Teams;
using LOL_GameAssistant.Infrastructure.Settings;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

public sealed class GameKdaPremadeAnnouncementTests
{
    [Theory]
    [InlineData(true, 0, "我方", "蓝甲、蓝乙")]
    [InlineData(true, 1, "我方", "蓝甲、蓝乙")]
    [InlineData(true, 2, "我方", "蓝丙、蓝丁")]
    [InlineData(true, 5, "敌方", "红甲、红乙")]
    [InlineData(true, 7, "敌方", "红丙、红丁")]
    [InlineData(false, 2, "敌方", "蓝丙、蓝丁")]
    [InlineData(false, 7, "我方", "红丙、红丁")]
    public void IndividualAnnouncementOnlyIncludesTheSelectedPlayersGroup(bool blueSide, int slot, string side, string names)
        => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = CreateFormWithMultipleGroups(blueSide);
        Assert.Equal($"【{side}开黑·2人】{names}", Assert.Single(form.BuildPremadeAnnouncementMessages(true, slot)));
        Assert.Equal(4, form.BuildPremadeAnnouncementMessages(true).Count);
    });

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(9)]
    [InlineData(10)]
    public void SoloOrMissingPlayerDoesNotIncludeOtherGroups(int slot) => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = CreateFormWithMultipleGroups(true);
        Assert.Empty(form.BuildPremadeAnnouncementMessages(true, slot));
        Assert.Empty(form.BuildPremadeAnnouncementMessages(false, 0));
    });

    private static LiveGameForm CreateFormWithMultipleGroups(bool blueSide)
    {
        var form = CreateForm(blueSide);
        Field<List<(string Puuid, string Name, string Team)>>(form, "_gameAssessmentRoster").AddRange([
            ("blue3", "蓝丙", "蓝方"), ("blue4", "蓝丁", "蓝方"), ("blue5", "蓝戊", "蓝方"),
            ("red3", "红丙", "红方"), ("red4", "红丁", "红方"), ("red5", "红戊", "红方")]);
        Set(form, "_currentPartyDetection", new CurrentPartyDetectionResult(new([
            new(1, ["blue1", "blue2"], ["蓝甲", "蓝乙"], 0),
            new(2, ["blue3", "blue4"], ["蓝丙", "蓝丁"], 0),
            new(3, ["red1", "red2"], ["红甲", "红乙"], 1),
            new(4, ["red3", "red4"], ["红丙", "红丁"], 1)
        ]), [true, true]));
        return form;
    }

    [Fact]
    public void MultipleGroupsHaveSeparateNumbersForEachSideInBothPhases() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = CreateForm(true);
        var roster = Field<List<(string Puuid, string Name, string Team)>>(form, "_gameAssessmentRoster");
        roster.AddRange([("blue3", "蓝丙", "蓝方"), ("blue4", "蓝丁", "蓝方"),
            ("red3", "红丙", "红方"), ("red4", "红丁", "红方")]);
        // 检测返回顺序不决定编号；每个阵营按当前玩家位置从一队开始。
        Set(form, "_currentPartyDetection", new CurrentPartyDetectionResult(new([
            new(1, ["red3", "red4"], ["红丙", "红丁"], 1),
            new(2, ["blue3", "blue4"], ["蓝丙", "蓝丁"], 0),
            new(3, ["red1", "red2"], ["红甲", "红乙"], 1),
            new(4, ["blue1", "blue2"], ["蓝甲", "蓝乙"], 0)
        ]), [true, true]));
        string[] expected = ["【我方开黑一队·2人】蓝甲、蓝乙", "【我方开黑二队·2人】蓝丙、蓝丁",
            "【敌方开黑一队·2人】红甲、红乙", "【敌方开黑二队·2人】红丙、红丁"];
        Assert.Equal(expected, form.BuildPremadeAnnouncementMessages(true));
        var session = new ChampionSelectionSnapshot
        {
            MyTeam = Enumerable.Range(1, 4).Select(i => new ChampionSelectionMember { Puuid = $"blue{i}", CellId = i - 1 }).ToArray(),
            TheirTeam = Enumerable.Range(1, 4).Select(i => new ChampionSelectionMember { Puuid = $"red{i}", CellId = i + 4 }).ToArray()
        };
        Assert.Equal(string.Join("\n", expected.Append("KDA")), form.AddChampSelectPremadeMessages("KDA", session, true));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChampSelectListsBothSidesBeforeKdaWithoutGameRoster(bool inferred) => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = CreateForm(true);
        Field<List<(string Puuid, string Name, string Team)>>(form, "_gameAssessmentRoster").Clear();
        Set(form, "_currentPartyDetection", new CurrentPartyDetectionResult(new([
            new(1, ["blue1", "blue2"], ["我方甲", "我方乙"], 0),
            new(2, ["red1", "red2"], ["敌方甲", "敌方乙"], 1, inferred)
        ]), [true, true]));
        var session = new ChampionSelectionSnapshot
        {
            MyTeam = [new() { Puuid = "blue1" }, new() { Puuid = "blue2" }],
            TheirTeam = [new() { Puuid = "red1", CellId = 5 }, new() { Puuid = "red2", CellId = 6 }]
        };
        string kda = "【近期KDA】\n我方战绩";
        string message = form.AddChampSelectPremadeMessages(kda, session, true);
        Assert.Equal($"【我方开黑·2人】我方甲、我方乙\n【敌方开黑·2人】敌方甲、敌方乙\n{kda}", message);
        Assert.Equal(kda, form.AddChampSelectPremadeMessages(kda, session, false));
        var hiddenEnemies = new ChampionSelectionSnapshot { MyTeam = session.MyTeam };
        Assert.Equal($"【我方开黑·2人】我方甲、我方乙\n{kda}", form.AddChampSelectPremadeMessages(kda, hiddenEnemies, true));
    });

    [Fact]
    public void HiddenEnemyRosterOrNoPremadeAddsNothingInChampSelect() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = CreateForm(true);
        Set(form, "_currentPartyDetection", new CurrentPartyDetectionResult(new([
            new(1, ["red1", "red2"], ["敌方甲", "敌方乙"], 1)
        ]), [true, true]));
        const string kda = "【近期KDA】\n我方战绩";
        Assert.Equal(kda, form.AddChampSelectPremadeMessages(kda, new(), true));
        Set(form, "_currentPartyDetection", new CurrentPartyDetectionResult(new([]), [true, true]));
        var session = new ChampionSelectionSnapshot { TheirTeam = [new() { Puuid = "red1" }, new() { Puuid = "red2" }] };
        Assert.Equal(kda, form.AddChampSelectPremadeMessages(kda, session, true));
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BothSidesAreLabelledCorrectlyAndPrefixesPrecedeKda(bool blueSide) => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = CreateForm(blueSide);
        Set(form, "_currentPartyDetection", new CurrentPartyDetectionResult(new([
            new(1, ["blue1", "blue2"], ["蓝甲", "蓝乙"], 0),
            new(2, ["red1", "red2"], ["红甲", "红乙"], 1)
        ]), [true, true]));
        string enemy = blueSide ? "红" : "蓝";
        string ally = blueSide ? "蓝" : "红";
        var prefixes = form.BuildPremadeAnnouncementMessages(true);
        Assert.Equal(new[] { $"【我方开黑·2人】{ally}甲、{ally}乙", $"【敌方开黑·2人】{enemy}甲、{enemy}乙" }, prefixes);
        var messages = (IReadOnlyList<string>)typeof(LiveGameForm).GetMethod("BuildCurrentGameKdaMessages",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [false])!;
        Assert.Equal(prefixes[0], messages[0]);
        Assert.Equal(prefixes[1], messages[1]);
        Assert.StartsWith("【蓝方 蓝甲：", messages[2]);
        Assert.Equal(4, messages.Count);
    });

    [Fact]
    public void AllyOnlyGroupIsIncludedButDisabledOrUnknownSelfProducesNoExtraMessage() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = CreateForm(true);
        Set(form, "_currentPartyDetection", new CurrentPartyDetectionResult(new([
            new(1, ["blue1", "blue2"], ["蓝甲", "蓝乙"], 0)
        ]), [true, true]));
        Assert.Equal("【我方开黑·2人】蓝甲、蓝乙", Assert.Single(form.BuildPremadeAnnouncementMessages(true)));
        Set(form, "_currentPartyDetection", new CurrentPartyDetectionResult(new([
            new(1, ["red1", "red2"], ["红甲", "红乙"], 1)
        ]), [true, true]));
        Assert.Empty(form.BuildPremadeAnnouncementMessages(false));
        Set(form, "_encounterSelfPuuid", null);
        Assert.Empty(form.BuildPremadeAnnouncementMessages(true));
    });

    [Fact]
    public void HistoricalInferenceDisplaysPremadeAndUsesLatestNames() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = CreateForm(true);
        Set(form, "_currentPartyDetection", new CurrentPartyDetectionResult(new([]), [false, false]));
        Set(form, "_historyParties", new PremadeDetectionResult([
            new(1, ["red1", "red2"], ["旧名称", "加载玩家信息…"], 1, true)
        ]));
        var assessments = Field<Dictionary<string, PlayerRecentPerformanceEventArgs>>(form, "_gameAssessments");
        assessments["red1"] = new("red1", "新名称\n", false, null);
        string message = Assert.Single(form.BuildPremadeAnnouncementMessages(true));
        Assert.Equal("【敌方开黑·2人】新名称、红乙", message);
    });

    [Fact]
    public void StaleGroupCannotNamePlayersOutsideCurrentRoster() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = CreateForm(true);
        Set(form, "_currentPartyDetection", new CurrentPartyDetectionResult(new([
            new(1, ["red1", "old-player"], ["红甲", "上一局玩家"], 1)
        ]), [true, true]));
        Assert.Empty(form.BuildPremadeAnnouncementMessages(true));
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TogglePersistsThroughLegacySettingsMapping(bool enabled)
    {
        var original = new AssistantSettings { GameKdaPremadeAnnouncementEnabled = enabled };
        Assert.Equal(enabled, LegacySettingsMapper.ToDomain(LegacySettingsMapper.ToLegacy(original)).GameKdaPremadeAnnouncementEnabled);
    }

    private static LiveGameForm CreateForm(bool blueSide)
    {
        var form = new LiveGameForm(Service<ILobbyService>((_, _) => null),
            Service<IPlayerProfileService>((_, _) => null), Service<IChampionSelectService>((_, _) => null));
        var roster = Field<List<(string Puuid, string Name, string Team)>>(form, "_gameAssessmentRoster");
        roster.AddRange([("blue1", "蓝甲", "蓝方"), ("blue2", "蓝乙", "蓝方"),
            ("red1", "红甲", "红方"), ("red2", "红乙", "红方")]);
        Set(form, "_encounterSelfPuuid", blueSide ? "blue1" : "red1");
        return form;
    }

    private static void Set(object target, string name, object? value) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
}
