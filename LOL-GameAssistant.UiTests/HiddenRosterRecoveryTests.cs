using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using Newtonsoft.Json;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class HiddenRosterRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompleteSelectionsRestoreThreeHiddenPlayersOnEitherSide(bool hiddenBlue)
    {
        var raw = JsonConvert.SerializeObject(new
        {
            phase = "InProgress",
            gameData = new
            {
                gameId = 123L,
                queue = new { id = 2400, gameMode = "ARAM", numPlayersPerTeam = 5 },
                teamOne = Enumerable.Range(1, hiddenBlue ? 2 : 5).Select(Member),
                teamTwo = Enumerable.Range(6, hiddenBlue ? 5 : 2).Select(Member),
                // 故意打乱完整记录，避免按数组前后五人推断队伍。
                playerChampionSelections = Enumerable.Range(1, 10).Reverse().Select(i => new
                { puuid = $"player{i}", championId = i })
            }
        });
        var session = LegacyLobbyService.MapCurrentSession(JsonConvert.DeserializeObject<GameSessionResponse>(raw)!);
        Assert.Equal(5, session.TeamOne.Count);
        Assert.Equal(5, session.TeamTwo.Count);
        Assert.Equal(Enumerable.Range(1, 5).Select(i => $"player{i}"), session.TeamOne.Select(m => m.Puuid).OrderBy(id => int.Parse(id[6..])));
        Assert.Equal(Enumerable.Range(6, 5).Select(i => $"player{i}"), session.TeamTwo.Select(m => m.Puuid).OrderBy(id => int.Parse(id[6..])));
        Assert.Equal(10, session.TeamOne.Concat(session.TeamTwo).Select(m => m.Puuid).Distinct().Count());
        Assert.All(session.TeamOne.Concat(session.TeamTwo), m => Assert.Equal(int.Parse(m.Puuid[6..]), m.ChampionId));
        Assert.Equal("party", session.TeamOne.First(m => m.Puuid == "player1").PartyId);
        Assert.Equal(2400, session.QueueId);
        Assert.Equal("ARAM", session.GameMode);
        Assert.Equal(123, session.GameId);
    }

    [Theory]
    [InlineData(4, 2, 10, 5)] // 双方都不完整，不能确定隐藏成员分队。
    [InlineData(5, 2, 9, 5)] // 完整记录尚未到齐。
    [InlineData(5, 2, 10, 0)] // 队列未提供人数。
    [InlineData(5, 5, 10, 5)] // 双方已完整，不增加重复成员。
    public void AmbiguousOrCompleteRostersRemainUnchanged(int blue, int red, int selections, int size)
    {
        var data = Data(blue, red, selections, size);
        var session = LegacyLobbyService.MapCurrentSession(new() { GameData = data });
        Assert.Equal(blue, session.TeamOne.Count);
        Assert.Equal(red, session.TeamTwo.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DuplicateOrUnrelatedSelectionsDoNotInventPlayers(bool duplicate)
    {
        var data = Data(5, 2, 10, 5);
        data.PlayerChampionSelections[0].Puuid = duplicate ? "player2" : "unrelated";
        var session = LegacyLobbyService.MapCurrentSession(new() { GameData = data });
        Assert.Equal(5, session.TeamOne.Count);
        Assert.Equal(2, session.TeamTwo.Count);
    }

    private static object Member(int i) => new { puuid = $"player{i}", championId = i, partyId = "party" };
    private static GameData Data(int blue, int red, int selections, int size) => new()
    {
        Queue = new() { NumPlayersPerTeam = size },
        TeamOne = Enumerable.Range(1, blue).Select(i => new TeamMember { Puuid = $"player{i}" }).ToList(),
        TeamTwo = Enumerable.Range(6, red).Select(i => new TeamMember { Puuid = $"player{i}" }).ToList(),
        PlayerChampionSelections = Enumerable.Range(1, selections).Select(i => new TeamMember { Puuid = $"player{i}" }).ToList()
    };
}
