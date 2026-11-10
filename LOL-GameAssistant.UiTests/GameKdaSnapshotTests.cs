using System.Reflection;
using LOL_GameAssistant.Application.ChampionSelect;
using LOL_GameAssistant.Application.Lobby;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.MatchAnalysis;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class GameKdaSnapshotTests
{
    [Fact]
    public void IndividualSlotsNeverShiftRedPlayersIntoMissingBlueSlots() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = new LiveGameForm(Service<ILobbyService>(), Service<IPlayerProfileService>(), Service<IChampionSelectService>());
        var roster = Field<List<(string Puuid, string Name, string Team)>>(form, "_gameAssessmentRoster");
        roster.Add(("blue", "蓝方一号", "蓝方"));
        for (int i = 1; i <= 5; i++) roster.Add(($"red{i}", $"红方{i}号", "红方"));
        string? Message(int slot) => (string?)typeof(LiveGameForm).GetMethod("BuildPlayerKdaMessage",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [slot]);
        Assert.Contains("蓝方一号", Message(0));
        Assert.Null(Message(1));
        Assert.Contains("红方1号：", Message(5));
        Assert.Contains("红方5号：", Message(9));
        Assert.Null(Message(10));
        Assert.DoesNotContain("红方2号", Message(5));
    });

    [Fact]
    public void LatePerformanceReplacesPendingMessageForBothTeams() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = new LiveGameForm(Service<ILobbyService>(), Service<IPlayerProfileService>(), Service<IChampionSelectService>());
        var roster = Field<List<(string Puuid, string Name, string Team)>>(form, "_gameAssessmentRoster");
        roster.Add(("blue", "玩家甲", "蓝方"));
        roster.Add(("red", "玩家乙", "红方"));
        var assessments = Field<Dictionary<string, PlayerRecentPerformanceEventArgs>>(form, "_gameAssessments");
        IReadOnlyList<string> Messages() => (IReadOnlyList<string>)typeof(LiveGameForm)
            .GetMethod("BuildCurrentGameKdaMessages", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [true])!;
        Assert.All(Messages(), message => Assert.Contains("战绩暂未获取", message));
        var assessment = new RecentModePerformanceAssessment(MatchPerformanceTier.Upper, 88, 20, 55, "", 4.9, true, RecentPerformanceLabel.Upper);
        assessments["blue"] = new("blue", "玩家甲", true, assessment);
        assessments["red"] = new("red", "玩家乙", false, assessment);
        var updated = Messages();
        Assert.Equal(2, updated.Count);
        string merged = string.Concat(updated);
        Assert.Contains("玩家甲：上等马88分｜KDA4.90｜胜率55%", merged);
        Assert.Contains("玩家乙：上等马88分｜KDA4.90｜胜率55%", merged);
        Assert.DoesNotContain(updated, message => message.Contains("暂未获取"));
    });

    [Fact]
    public void FullRosterSendsAllBlueBeforeRedEvenWithoutEnemyAssessments() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = new LiveGameForm(Service<ILobbyService>(), Service<IPlayerProfileService>(), Service<IChampionSelectService>());
        var roster = Field<List<(string Puuid, string Name, string Team)>>(form, "_gameAssessmentRoster");
        for (int i = 1; i <= 5; i++) roster.Add(($"blue{i}", $"蓝人{i}", "蓝方"));
        for (int i = 1; i <= 5; i++) roster.Add(($"red{i}", $"红人{i}", "红方"));
        var messages = (IReadOnlyList<string>)typeof(LiveGameForm).GetMethod("BuildCurrentGameKdaMessages",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, [false])!;
        Assert.Equal(6, messages.Count);
        Assert.Contains("【蓝方 蓝人1：", messages[0]); Assert.Contains("【蓝方 蓝人3：", messages[1]);
        Assert.Contains("【蓝方 蓝人5：", messages[2]); Assert.Contains("【红方 红人1：", messages[3]);
        Assert.Contains("【红方 红人3：", messages[4]); Assert.Contains("【红方 红人5：", messages[5]);
        Assert.Equal(new[] { 2, 2, 1, 2, 2, 1 }, messages.Select(message => message.Count(c => c == '【')));
        foreach (var player in roster) Assert.Single(messages, message => message.Contains(player.Name + "：", StringComparison.Ordinal));
    });

    private static T Field<T>(object target, string name) => (T)target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static T Service<T>() where T : class
    {
        T proxy = DispatchProxy.Create<T, ChampSelectCompanionTests.ServiceProxy>();
        ((ChampSelectCompanionTests.ServiceProxy)(object)proxy).Handler = (_, _) => null;
        return proxy;
    }
}
