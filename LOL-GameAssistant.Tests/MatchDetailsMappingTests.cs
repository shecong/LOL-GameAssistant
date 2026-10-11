using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Infrastructure.GameData;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class MatchDetailsMappingTests
{
    [Theory]
    [InlineData("Win", true)]
    [InlineData("Fail", false)]
    [InlineData("false", false)]
    public void MissingPersonalResultUsesPlayersOwnTeamInSummaryAndDetail(string result, bool expected)
    {
        var detail = LegacyMatchReadModelMapper.ToDomain(new GameDetailModel.GameInfo
        {
            participants = [new() { participantId = 2, teamId = 200, stats = new() }],
            teams = [new() { teamId = 100, win = "Win" }, new() { teamId = 200, win = result }]
        })!;
        var summary = LegacyMatchReadModelMapper.ToDomain(new GameHeadModel.MatchHistoryResponse
        {
            Games = new() { Games = [new()
            {
                Participants = [new() { ParticipantId = 2, TeamId = 200, Stats = new() }],
                Teams = [new() { TeamId = 100, Win = "Win" }, new() { TeamId = 200, Win = result }]
            }] }
        })!;
        foreach (var stats in new[] { detail.participants[0].stats!, summary.Games!.Games[0].Participants[0].stats! })
        {
            Assert.True(stats.HasWinResult);
            Assert.Equal(expected, stats.Win);
            Assert.False(stats.HasKdaResult);
        }
    }

    [Theory]
    [InlineData(0, "Win")]
    [InlineData(200, "unknown")]
    [InlineData(300, "Fail")]
    public void UnknownOrUnmatchedTeamCannotInventWinResult(int teamId, string result)
    {
        var detail = LegacyMatchReadModelMapper.ToDomain(new GameDetailModel.GameInfo
        {
            participants = [new() { teamId = teamId, stats = new() }],
            teams = [new() { teamId = 200, win = result }]
        })!;
        Assert.False(detail.participants[0].stats!.HasWinResult);
    }

    [Fact]
    public void ConfirmedPersonalLossIsPreserved()
    {
        var detail = LegacyMatchReadModelMapper.ToDomain(new GameDetailModel.GameInfo
        {
            participants = [new() { teamId = 200, stats = new() { win = false } }],
            teams = [new() { teamId = 200, win = "Win" }]
        })!;
        Assert.True(detail.participants[0].stats!.HasWinResult);
        Assert.False(detail.participants[0].stats!.Win);
    }

    [Fact]
    public void MapsBansAndHextechAugmentsFromMatchDetail()
    {
        var source = new GameDetailModel.GameInfo
        {
            gameMode = "CLASSIC",
            queueId = "2400",
            teams =
            [
                new GameDetailModel.TeamsItem
                {
                    teamId = 100,
                    bans =
                    [
                        new GameDetailModel.BansItem { championId = 99, pickTurn = 2 },
                        new GameDetailModel.BansItem { championId = -1, pickTurn = 1 },
                        new GameDetailModel.BansItem { championId = 25, pickTurn = 0 }
                    ]
                }
            ],
            participants =
            [
                new GameDetailModel.ParticipantsItem
                {
                    participantId = 1,
                    teamId = 100,
                    stats = new GameDetailModel.Stats
                    {
                        playerAugment1 = 1001,
                        playerAugment2 = 0,
                        playerAugment3 = 1002
                    }
                }
            ]
        };

        var detail = Assert.IsType<LOL_GameAssistant.Domain.Matches.MatchDetail>(
            LegacyMatchReadModelMapper.ToDomain(source));

        Assert.Equal([25, 99], detail.GetBannedChampionIds(100));
        Assert.Equal([1001, 1002], detail.participants[0].stats!.AugmentIds);
        Assert.True(detail.IsAugmentAram());
        Assert.Equal("海克斯大乱斗", detail.GetModeText());
        Assert.Equal("海克斯大乱斗", new MatchHistoryGame { QueueId = 2400 }.GetModeText());
        Assert.Equal("海克斯大乱斗", new MatchHistoryGame { QueueId = 3270 }.GetModeText());
    }

    [Fact]
    public async Task MapsMayhemAugmentIdsToChineseNamesOffline()
    {
        var names = await AugmentCatalog.ResolveAsync([1001, 1002, 1004]);
        Assert.Equal(["泰坦的坚决", "尖端发明家", "回归基本功"],
            names.Select(item => item.Name).ToArray());
    }
}
