using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Helper;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class HomeRecordCompactLayoutTests
{
    [Theory]
    [InlineData(999, "999")]
    [InlineData(1000, "1k")]
    [InlineData(9804, "9.8k")]
    [InlineData(48901, "48.9k")]
    public void CompactNumbersKeepOneDecimal(int value, string expected) =>
        Assert.Equal(expected, RecordForm.FormatCompactNumber(value));

    [Theory]
    [InlineData(620, 4)]
    [InlineData(860, 4)]
    [InlineData(1060, 4)]
    [InlineData(1060, 8)]
    public void AllPlayersAndSelectedAugmentsFitWithoutOverlapping(int width, int augmentCount) =>
        MatchListScrollingTests.OnUiThread(() =>
        {
            var detail = UiRedesignTests.SampleMatch(true);
            detail.queueId = "2400";
            detail.gameMode = "KIWI";
            var stats = detail.participants[0].stats!;
            stats.champLevel = 18;
            stats.totalMinionsKilled = 81;
            stats.neutralMinionsKilled = 7;
            stats.totalDamageDealtToChampions = 48901;
            stats.goldEarned = 19908;
            stats.AugmentIds = Enumerable.Range(900001, augmentCount).ToList();
            using var card = new RecordForm(
                Service<IMatchHistoryService>((_, _) => Task.FromResult<MatchDetail?>(detail)),
                Service<IGameAssetService>((_, _) => Task.FromResult<GameAsset?>(null))) { Width = width };
            card.setInfo(new MatchHistoryGame { Participants = detail.participants }, "player-1", detail)
                .GetAwaiter().GetResult();
            card.PerformLayout();

            Assert.DoesNotContain("召唤师", Field<Control>(card, "game_name").Text);
            Assert.Equal("补刀 88", Field<Control>(card, "game_cs").Text);
            Assert.Equal("伤害 48.9k", Field<Control>(card, "game_damage").Text);
            Assert.Equal("金币 19.9k", Field<Control>(card, "game_gold").Text);
            Assert.False(Field<Control>(card, "_performanceTag").Visible);
            Assert.Equal("强化", Field<Control>(card, "_loadoutLabel").Text);
            var loadout = Field<Control>(card, "_selectedLoadout");
            Assert.Equal(augmentCount, loadout.Controls.Count);
            foreach (Control tag in loadout.Controls)
                Assert.True(loadout.ClientRectangle.Contains(tag.Bounds), $"强化超出边界：{tag.Bounds}");
            foreach (string teamName in new[] { "flowAlly", "flowEnemy" })
            {
                var team = Field<Control>(card, teamName);
                Assert.Equal(5, team.Controls.Count);
                Assert.True(card.ClientRectangle.Contains(team.Bounds));
                foreach (Control player in team.Controls)
                    Assert.True(team.ClientRectangle.Contains(player.Bounds));
                Assert.False(team.Bounds.IntersectsWith(loadout.Bounds));
            }
            foreach (string name in new[] { "game_cs", "game_damage", "game_gold" })
            {
                var control = Field<Control>(card, name);
                Assert.Equal(Field<Control>(card, "game_cs").Top, control.Top);
                Assert.True(control.Bottom <= Field<Control>(card, "pic_1").Top);
            }
            if (width >= 860 && augmentCount == 4) Assert.True(card.Height <= 150 * card.DeviceDpi / 96);
            string? output = Environment.GetEnvironmentVariable("HOME_RECORD_PREVIEW");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Directory.CreateDirectory(output);
                string previous = UiTheme.Mode;
                try
                {
                    UiTheme.SetMode("Dark");
                    card.ApplyTheme(UiTheme.Palette);
                    card.CreateControl();
                    using var bitmap = new Bitmap(card.Width, card.Height);
                    card.DrawToBitmap(bitmap, card.ClientRectangle);
                    bitmap.Save(Path.Combine(output, $"home-record-{width}-{augmentCount}.png"));
                }
                finally { UiTheme.SetMode(previous); }
            }
        });
}
