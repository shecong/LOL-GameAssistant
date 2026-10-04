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
        Assert.Contains("【红1】红方1号", Message(5));
        Assert.Contains("【红5】红方5号", Message(9));
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
        Assert.Contains(updated, message => message.StartsWith("蓝 ") && message.Contains("K4.9"));
        Assert.Contains(updated, message => message.StartsWith("红 ") && message.Contains("K4.9"));
        Assert.DoesNotContain(updated, message => message.Contains("暂未获取"));
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
