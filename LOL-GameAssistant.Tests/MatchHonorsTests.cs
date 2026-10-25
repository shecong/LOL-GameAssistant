using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class MatchHonorsTests
{
    internal static MatchHistoryGame Game() => new()
    {
        HasFullParticipants = true, GameId = 123, GameDuration = 1800, QueueId = 420,
        Participants = Enumerable.Range(1, 10).Select(id => new MatchParticipant {
            participantId = id, championId = id, teamId = id <= 5 ? 100 : 200,
            stats = new() { Win = id <= 5, kills = id % 5 == 1 ? 12 : 2, deaths = 3, assists = 4,
                HasDamageResult = true, totalDamageDealtToChampions = id % 5 == 1 ? 40000 : 10000 }
        }).ToList(),
        ParticipantIdentities = Enumerable.Range(1, 10).Select(id => new MatchIdentity {
            ParticipantId = id, Player = new() { Puuid = "p" + id }
        }).ToList()
    };

    [Fact]
    public void FullRosterAwardsExactlyOneMvpOnEachSide()
    {
        var game = Game();
        var awards = game.Participants.Select(p => MatchHonorCalculator.ForPlayer(game, p)).ToArray();
        Assert.Single(awards, a => a.Mvp == "胜方 MVP");
        Assert.Single(awards, a => a.Mvp == "败方 MVP");
        Assert.Equal("胜方 MVP", awards[0].Mvp);
        Assert.Equal("败方 MVP", awards[5].Mvp);
        Assert.All(awards, a => Assert.True(a.MvpEvaluated));
        var detail = game.ToSummaryDetail();
        Assert.Equal(awards[0].Text, MatchHonorCalculator.ForPlayer(detail, detail.participants[0]).Text);
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("kda")]
    [InlineData("win")]
    [InlineData("mixed_team_result")]
    [InlineData("duplicate")]
    [InlineData("remake")]
    [InlineData("short")]
    [InlineData("arena")]
    public void IncompleteOrInvalidComparisonsNeverInventMvp(string scenario)
    {
        var game = Game();
        switch (scenario)
        {
            case "partial": game.HasFullParticipants = false; break;
            case "kda": game.Participants[3].stats!.HasKdaResult = false; break;
            case "win": game.Participants[3].stats!.HasWinResult = false; break;
            case "mixed_team_result": game.Participants[3].stats!.Win = false; break;
            case "duplicate": game.Participants[3].participantId = 1; break;
            case "remake": game.EndOfGameResult = "Abort_Unexpected"; break;
            case "short": game.GameDuration = 200; break;
            case "arena": game.Participants[3].teamId = 300; break;
        }
        Assert.All(game.Participants, p => Assert.Null(MatchHonorCalculator.ForPlayer(game, p).Mvp));
    }

    [Fact]
    public void UnknownRosterStillShowsConfirmedIndividualFeats()
    {
        var game = Game(); game.HasFullParticipants = false;
        var mine = game.Participants[0];
        mine.stats!.largestKillingSpree = 8; mine.stats.pentaKills = 2; mine.stats.quadraKills = 1;
        var award = MatchHonorCalculator.ForPlayer(game, mine);
        Assert.Equal(new[] { "超神", "五杀×2" }, award.Feats);
        Assert.Null(award.Mvp); Assert.False(award.MvpEvaluated);
        mine.stats.largestKillingSpree = 7; mine.stats.pentaKills = 0;
        Assert.Equal(new[] { "四杀" }, MatchHonorCalculator.ForPlayer(game, mine).Feats);
    }

    [Fact]
    public void TiesAreStableRegardlessOfRosterOrder()
    {
        var game = Game();
        foreach (var p in game.Participants) { p.stats!.kills = 3; p.stats.totalDamageDealtToChampions = 10000; }
        var mine = game.Participants[0];
        game.Participants.Reverse();
        Assert.Equal("胜方 MVP", MatchHonorCalculator.ForPlayer(game, mine).Mvp);
    }

    [Fact]
    public void MissingOptionalMetricIsOmittedForWholeTeam()
    {
        var game = Game();
        foreach (var p in game.Participants) { p.stats!.kills = 3; p.stats.HasDamageResult = false; }
        var first = game.Participants[0];
        var other = game.Participants[1]; other.stats!.HasDamageResult = true; other.stats.totalDamageDealtToChampions = int.MaxValue;
        Assert.Equal("胜方 MVP", MatchHonorCalculator.ForPlayer(game, first).Mvp);
        Assert.Null(MatchHonorCalculator.ForPlayer(game, other).Mvp);
    }

    [Fact]
    public void LegacySummaryAndDetailPreserveKillingSpree()
    {
        var raw = new GameHeadModel.MatchHistoryResponse { Games = new() { Games = [new() {
            Participants = [new() { Stats = new() { LargestKillingSpree = 8, PentaKills = 1 } }]
        }] } };
        var summary = LegacyMatchReadModelMapper.ToDomain(raw)!.Games!.Games[0];
        Assert.Equal(8, summary.Participants[0].stats!.largestKillingSpree);
        var detail = LegacyMatchReadModelMapper.ToDomain(new GameDetailModel.GameInfo {
            endOfGameResult = "Abort_Unexpected",
            participants = [new() { stats = new() { largestKillingSpree = 9, quadraKills = 1 } }]
        })!;
        Assert.Equal(9, detail.participants[0].stats!.largestKillingSpree);
        Assert.True(detail.HasFullParticipants);
        Assert.Equal("Abort_Unexpected", detail.endOfGameResult);
        Assert.False(summary.ToSummaryDetail().HasFullParticipants);
    }
}
