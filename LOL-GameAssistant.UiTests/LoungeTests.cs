using System.Reflection;
using LOL_GameAssistant;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.Lounge;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using LOL_GameAssistant.Application.LeagueClient;
using Newtonsoft.Json.Linq;
using Xunit;
using LOL.Lounge;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class LoungeTests
{
    [Fact]
    public void LoungeMenuImmediatelyPrecedesAboutAndPageSupportsBothThemes() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var main = new GameMain();
        var tabs = (AntdUI.Tabs)typeof(GameMain).GetField("tabs1", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
        var labels = tabs.Pages.Select(p => p.Text).ToArray();
        Assert.Equal("lol休息室", labels[Array.IndexOf(labels, "关于") - 1]);
        using var client = new LoungeClient(new LoungeLcuReader(new LcuHttpRequestSender()));
        using var page = new LoungeForm(client);
        using var host = new Form { ClientSize = new Size(1100, 700), ShowInTaskbar = false };
        host.Controls.Add(page);
        host.Show();
        foreach (string mode in new[] { "Dark", "Light" })
        {
            UiTheme.SetMode(mode);
            UiTheme.Apply(page); // Also proves IThemeAware does not recursively re-enter Apply.
            Assert.Equal(UiTheme.Palette.Surface, page.BackColor);
        }
        Assert.True(page.Width > 1000);
        string? preview = Environment.GetEnvironmentVariable("LOL_LOUNGE_PREVIEW_PATH");
        if (!string.IsNullOrWhiteSpace(preview))
        {
            using var bitmap = new Bitmap(host.ClientSize.Width, host.ClientSize.Height);
            page.DrawToBitmap(bitmap, new Rectangle(Point.Empty, host.ClientSize));
            bitmap.Save(preview, System.Drawing.Imaging.ImageFormat.Png);
        }
        UiTheme.SetMode("System");
    });

    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("http://user:password@example.com")]
    [InlineData("https://user:password@example.com")]
    [InlineData("http://example.com?token=secret")]
    [InlineData("https://example.com?token=secret")]
    [InlineData("http://example.com/#fragment")]
    public void ServerConfigurationRejectsCredentialsAndUnsupportedUrls(string url) =>
        Assert.Throws<ArgumentException>(() => LoungeSettings.ValidateUrl(url));

    [Theory]
    [InlineData("http://203.0.113.1:5000", "http://203.0.113.1:5000")]
    [InlineData(" http://example.com:5000/ ", "http://example.com:5000")]
    [InlineData("http://localhost:5000", "http://localhost:5000")]
    [InlineData("https://example.com/", "https://example.com")]
    public void ServerConfigurationAcceptsHttpAndHttps(string url, string expected) =>
        Assert.Equal(expected, LoungeSettings.ValidateUrl(url));

    [Fact]
    public void MemberCopySeparatesNameTagAndChannelIdentifier()
    {
        var member = new LOL.Lounge.LoungeMember("channel-id", "小明#12345", "艾欧尼亚", "在线");
        Assert.Equal("小明", LoungeForm.MemberCopyText(member, "名称"));
        Assert.Equal("12345", LoungeForm.MemberCopyText(member, "编号"));
        Assert.Equal("小明#12345", LoungeForm.MemberCopyText(member, "完整游戏ID"));
        Assert.Equal("channel-id", LoungeForm.MemberCopyText(member, "频道标识"));
        Assert.Empty(LoungeForm.MemberCopyText(member with { Name = "未知玩家" }, "编号"));
    }

    [Fact]
    public void RankUsesCurrentQueuesAndDoesNotInventUnrankedTier()
    {
        Assert.Empty(LoungeLcuReader.ReadRank(null));
        var ranked = JObject.Parse("""
            {"queueMap":{"RANKED_SOLO_5x5":{"tier":"EMERALD","division":"II","leaguePoints":42},
            "RANKED_FLEX_SR":{"tier":"UNRANKED"}}}
            """);
        Assert.Equal("单双排 翡翠II 42点", LoungeLcuReader.ReadRank(ranked));
    }

    [Fact]
    public void VictoryPartyMustBeObservedPremadeAndSameTeamInExactMatch()
    {
        var detail = JObject.Parse("""
            {"gameId":42,"participantIdentities":[
              {"participantId":1,"player":{"puuid":"self"}},
              {"participantId":2,"player":{"puuid":"friend","gameName":"队友","tagLine":"123"}},
              {"participantId":3,"player":{"puuid":"random","summonerName":"路人"}},
              {"participantId":4,"player":{"puuid":"enemy","summonerName":"对手"}}],
             "participants":[{"participantId":1,"teamId":100,"stats":{"win":true}},
              {"participantId":2,"teamId":100},{"participantId":3,"teamId":100},{"participantId":4,"teamId":200}]}
            """);
        Assert.Equal(new[] { "队友#123" }, LoungeLcuReader.ReadPartyNames(detail, 42, "self", ["self", "friend", "enemy"]));
        Assert.Empty(LoungeLcuReader.ReadPartyNames(detail, 43, "self", ["friend"]));
        Assert.Empty(LoungeLcuReader.ReadPartyNames(detail, 42, "self", []));
        detail["participants"]![0]!["stats"]!["win"] = false;
        Assert.Empty(LoungeLcuReader.ReadPartyNames(detail, 42, "self", ["friend"]));
    }

    [Fact]
    public void LocalReaderRequiresExactMatchAndExactPlayerAndHonorsHiddenActivity()
    {
        var detail = JObject.Parse("""
            {"gameId":42,"participantIdentities":[{"participantId":1,"player":{"puuid":"alice"}}],
             "participants":[{"participantId":1,"stats":{"win":true}}]}
            """);
        Assert.True(LoungeLcuReader.ReadVictory(detail, 42, "alice"));
        Assert.Null(LoungeLcuReader.ReadVictory(detail, 43, "alice"));
        Assert.Null(LoungeLcuReader.ReadVictory(detail, 42, "bob"));
        detail["endOfGameResult"] = "Abort_TooFewPlayers";
        Assert.False(LoungeLcuReader.ReadVictory(detail, 42, "alice"));
        Assert.Equal("活动已隐藏", LoungeLcuReader.Status("offline", "InProgress", true));
        Assert.Equal("活动已隐藏", LoungeLcuReader.Status("chat", "InProgress", false));
        Assert.Equal("游戏中", LoungeLcuReader.Status("chat", "InProgress", true));
        Assert.Equal("艾欧尼亚", LoungeLcuReader.RegionName("HN1"));
        Assert.Contains("未知大区", LoungeLcuReader.RegionName("unrecognized"));
    }

    [Fact]
    public async Task LocalIdentityUsesLivePlatformMetadataAndClearsImmediatelyOnLogout()
    {
        var lcu = new LocalRequests();
        lcu.Responses["/lol-summoner/v1/current-summoner"] = """{"puuid":"alice","gameName":"Alice","tagLine":"123"}""";
        lcu.Responses["/lol-chat/v1/me"] = """{"availability":"chat","platformId":"HN1"}""";
        lcu.Responses["/lol-gameflow/v1/gameflow-phase"] = "\"InProgress\"";
        lcu.Responses["/lol-gameflow/v1/session"] = """{"gameData":{"gameId":42,"queue":{"id":450}}}""";
        var reader = new LoungeLcuReader(lcu);
        var state = await reader.ReadAsync(true, default);
        Assert.Equal("HN1", state.Presence.Platform);
        Assert.Equal("艾欧尼亚", state.Presence.Region);
        Assert.Equal("Alice#123", state.Presence.Name);
        Assert.Equal(42, state.GameId);
        lcu.Responses.Remove("/lol-summoner/v1/current-summoner");
        var loggedOut = await reader.ReadAsync(true, default);
        Assert.Empty(loggedOut.Presence.Puuid);
        Assert.Empty(loggedOut.Presence.Platform);
        Assert.Equal(0, loggedOut.GameId);
    }

    [Fact]
    public void UnreadAndMentionsIgnoreReplaySelfBlockedAndReading()
    {
        var inbox = new LoungeInbox();
        var message = new LoungeMessage(1, "", "other", "Alice", "HN1", "mention",
            LoungeContent.Serialize(new LoungeChatContent("@Bob hello", ["me"])), DateTimeOffset.UtcNow);
        Assert.False(inbox.Accept(message, "me", "self", false, false));
        Assert.True(inbox.Accept(message, "me", "self", false, true));
        Assert.False(inbox.Accept(message, "me", "self", false, true));
        Assert.Equal(1, inbox.Count);
        Assert.Equal(1, inbox.Mentions);
        Assert.False(inbox.Accept(message with { Id = 2, SenderId = "self" }, "me", "self", false, true));
        Assert.False(inbox.Accept(message with { Id = 3 }, "me", "self", true, true));
        inbox.Reading = true;
        Assert.False(inbox.Accept(message with { Id = 4 }, "me", "self", false, true));
        inbox.Clear();
        Assert.Equal(0, inbox.Count);
        Assert.Equal(0, inbox.Mentions);
    }

    [Fact]
    public void HighlightsUseExactPlayersAndKillingSpreeInsteadOfTotalKills()
    {
        var detail = JObject.Parse("""
            {"gameId":42,"participantIdentities":[{"participantId":1,"player":{"puuid":"alice"}}],
             "participants":[{"participantId":1,"stats":{"win":false,"kills":20,"pentaKills":1,"largestKillingSpree":8}}]}
            """);
        Assert.Equal(new[] { "pentakill", "legendary" }, LoungeLcuReader.ReadHighlights(detail, 42, "alice"));
        Assert.Empty(LoungeLcuReader.ReadHighlights(detail, 43, "alice"));
        Assert.Empty(LoungeLcuReader.ReadHighlights(detail, 42, "bob"));
        detail["participants"]![0]!["stats"]!["largestKillingSpree"] = 7;
        Assert.Equal(new[] { "pentakill" }, LoungeLcuReader.ReadHighlights(detail, 42, "alice"));
        detail["endOfGameResult"] = "Abort_TooFewPlayers";
        Assert.Empty(LoungeLcuReader.ReadHighlights(detail, 42, "alice"));
    }

    [Fact]
    public void PromotionScoresIgnoreLpAndSeparateQueues()
    {
        var before = JObject.Parse("""{"queueMap":{"RANKED_SOLO_5x5":{"tier":"GOLD","division":"II","leaguePoints":99}}}""");
        int score = LoungeLcuReader.ReadRankScores(before)["RANKED_SOLO_5x5"];
        before["queueMap"]!["RANKED_SOLO_5x5"]!["leaguePoints"] = 100;
        Assert.Equal(score, LoungeLcuReader.ReadRankScores(before)["RANKED_SOLO_5x5"]);
        before["queueMap"]!["RANKED_SOLO_5x5"]!["division"] = "I";
        Assert.True(LoungeLcuReader.ReadRankScores(before)["RANKED_SOLO_5x5"] > score);
        Assert.False(LoungeLcuReader.ReadRankScores(before).ContainsKey("RANKED_FLEX_SR"));
    }

    [Fact]
    public void PresetStickerIsRenderedAsAnImageAndFeatureLayoutFits() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var client = new LoungeClient(new LoungeLcuReader(new LocalRequests()));
        var receive = typeof(LoungeClient).GetMethod("Receive", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var recruit = new LoungeRecruitment("艾欧尼亚", 420, "翡翠", "辅助", 1, "轻松上分，友好交流，今晚一起赢！");
        var sample = new[]
        {
            new LoungeMessage(1, "", "a", "小明#123", "艾欧尼亚", "recruit", LoungeContent.Serialize(recruit), DateTimeOffset.Now),
            new LoungeMessage(2, "", "b", "小红#456", "艾欧尼亚", "chat", "我来辅助，一起加油！", DateTimeOffset.Now),
            new LoungeMessage(3, "", "a", "小明#123", "艾欧尼亚", "sticker", "nice", DateTimeOffset.Now)
        };
        foreach (var m in sample) receive.Invoke(client, [m, false]);
        using var page = new LoungeForm(client);
        using var host = new Form { ClientSize = new Size(1100, 780), ShowInTaskbar = false };
        host.Controls.Add(page); host.Show();
        UiTheme.SetMode("Dark"); UiTheme.Apply(page);
        System.Windows.Forms.Application.DoEvents();
        var history = (RichTextBox)typeof(LoungeForm).GetField("_history", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        Assert.Contains("\\pict", history.Rtf);
        Assert.True(history.Height > 300);
        Assert.True(history.GetPositionFromCharIndex(0).Y > -150, $"First Y: {history.GetPositionFromCharIndex(0).Y}, last Y: {history.GetPositionFromCharIndex(history.TextLength).Y}, height: {history.ClientSize.Height}"); // Images must not scroll a short history out of view.
        string? preview = Environment.GetEnvironmentVariable("LOL_LOUNGE_FEATURE_PREVIEW_PATH");
        if (!string.IsNullOrWhiteSpace(preview))
        {
            using var bitmap = new Bitmap(host.ClientSize.Width, host.ClientSize.Height);
            page.DrawToBitmap(bitmap, new Rectangle(Point.Empty, host.ClientSize));
            bitmap.Save(preview, System.Drawing.Imaging.ImageFormat.Png);
        }
        UiTheme.SetMode("System");
    });

    [Fact]
    public void FollowNotificationsUseNewArrivalsAndRespectManualAndInGameSilence()
    {
        using var client = new LoungeClient(new LoungeLcuReader(new LocalRequests()));
        var type = typeof(LoungeClient);
        void Set(string field, object value) => type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(client, value);
        var update = type.GetMethod("UpdateMembers", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void Members(params LoungeMember[] values) => update.Invoke(client, [values]);
        var alerts = new List<string>();
        client.Notification += (title, _) => alerts.Add(title);
        Set("_settings", new LoungeSettings(FollowedMembers: ["friend"], QuietInGame: true));
        var friend = new LoungeMember("friend", "Alice#123", "艾欧尼亚", "在线");
        Members(friend); // Initial replay is quiet.
        Set("_initialSync", true);
        Members(); Members(friend); Members(friend);
        Assert.Single(alerts);
        Set("_inGame", true);
        Members(); Members(friend);
        Assert.Single(alerts);
        Set("_inGame", false);
        Set("_settings", new LoungeSettings(FollowedMembers: ["friend"], DoNotDisturb: true));
        Members(); Members(friend);
        Assert.Single(alerts);
    }

    [Fact]
    public void StickerAfterLongHistoryRemainsVisibleAtTheBottom() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var client = new LoungeClient(new LoungeLcuReader(new LocalRequests()));
        var receive = typeof(LoungeClient).GetMethod("Receive", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (int i = 1; i <= 120; i++)
            receive.Invoke(client, [new LoungeMessage(i, "", "a", "Alice", "艾欧尼亚", "chat", $"历史消息 {i}", DateTimeOffset.Now), false]);
        using var page = new LoungeForm(client);
        using var host = new Form { ClientSize = new Size(1100, 700), ShowInTaskbar = false };
        host.Controls.Add(page); host.Show();
        System.Windows.Forms.Application.DoEvents();
        receive.Invoke(client, [new LoungeMessage(121, "", "a", "Alice", "艾欧尼亚", "sticker", "nice", DateTimeOffset.Now), true]);
        typeof(LoungeForm).GetMethod("RefreshView", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
        System.Windows.Forms.Application.DoEvents();
        var history = (RichTextBox)typeof(LoungeForm).GetField("_history", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        Assert.Contains("\\pict", history.Rtf);
        using var bitmap = new Bitmap(history.Width, history.Height);
        history.DrawToBitmap(bitmap, new Rectangle(Point.Empty, history.ClientSize));
        int blue = 0;
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < Math.Min(180, bitmap.Width); x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.B > 180 && pixel.R is > 30 and < 100 && pixel.G is > 70 and < 180) blue++;
            }
        Assert.True(blue > 5000, $"表情应可见，实际蓝色像素数 {blue}");
    });

    [Fact]
    public void LegacyServerDoesNotAdvertiseStickerSupport()
    {
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<LoungeSnapshot>("""{"Messages":[],"Members":[]}""")!;
        Assert.Equal(1, snapshot.ProtocolVersion);
        using var client = new LoungeClient(new LoungeLcuReader(new LocalRequests()));
        Assert.False(client.SupportsRichMessages);
        typeof(LoungeClient).GetField("_protocolVersion", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(client, LoungeRules.ProtocolVersion);
        Assert.True(client.SupportsRichMessages);
    }

    [Fact]
    public void ProtocolTwoKeepsStickersButRequiresUpgradeForFullRecruitment()
    {
        using var client = new LoungeClient(new LoungeLcuReader(new LocalRequests()));
        typeof(LoungeClient).GetField("_protocolVersion", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(client, 2);
        Assert.True(client.SupportsRichMessages);
        Assert.False(client.SupportsRecruitment);
        Assert.Equal("消息不能为空，且不能超过 500 字。", LoungeClient.SendError(new Microsoft.AspNetCore.SignalR.HubException(
            "An unexpected error occurred invoking 'SendMessage' on the server. HubException: 消息不能为空，且不能超过 500 字。")));
    }

    [Fact]
    public void RecruitmentUpdatesRejectStaleBroadcastsAndDeletedRecruitments()
    {
        using var client = new LoungeClient(new LoungeLcuReader(new LocalRequests()));
        var receive = typeof(LoungeClient).GetMethod("ReceiveRecruitment", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var state = new LoungeRecruitmentState(42, "alice", "Alice#123",
            new("艾欧尼亚", 420, "翡翠", "辅助", 1), [], false, DateTimeOffset.UtcNow.AddMinutes(30));
        receive.Invoke(client, [state with { Closed = true, Revision = 3 }]);
        receive.Invoke(client, [state]);
        Assert.Equal("已结束", Assert.Single(client.Recruitments).Status);
        typeof(LoungeClient).GetMethod("DeleteLocal", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(client, [42L]);
        receive.Invoke(client, [state with { Revision = 4 }]);
        Assert.Empty(client.Recruitments);
    }

    [Fact]
    public void RecruitmentDialogRetainsInputOnFailureAndDetailsShowLiveCapacity() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var client = new LoungeClient(new LoungeLcuReader(new LocalRequests()));
        using var page = new LoungeForm(client);
        using var dialog = page.CreateRecruitmentDialog();
        T Find<T>(Control root, string name) where T : Control => (T)root.Controls.Find(name, true).Single();
        dialog.Show();
        System.Windows.Forms.Application.DoEvents();
        var note = Find<AntdUI.Input>(dialog, "Note");
        var publish = Find<AntdUI.Button>(dialog, "Publish");
        note.Text = "1111";
        publish.PerformClick(); // A disconnected publish must give feedback and retain the draft.
        System.Windows.Forms.Application.DoEvents();
        Assert.Contains("尚未连接", Find<AntdUI.Label>(dialog, "Feedback").Text);
        Assert.Equal("1111", note.Text);
        Assert.True(note.Enabled && publish.Enabled);
        Assert.True(note.Height >= 50);
        Assert.True(publish.Bottom <= publish.Parent!.ClientSize.Height);
        var receive = typeof(LoungeClient).GetMethod("ReceiveRecruitment", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var state = new LoungeRecruitmentState(42, "alice", "Alice#123",
            new("艾欧尼亚", 420, "翡翠", "辅助", 1, "友好交流"),
            [new("bob", "Bob#456", "艾欧尼亚", "在线")], false, DateTimeOffset.UtcNow.AddMinutes(30));
        receive.Invoke(client, [state]);
        using var detail = page.CreateRecruitmentDetails(42);
        detail.Show(); System.Windows.Forms.Application.DoEvents();
        Assert.Contains("已满员", Find<AntdUI.Input>(detail, "Description").Text);
        Assert.Contains("1/1", Find<AntdUI.Input>(detail, "Description").Text);
        Assert.True(Find<AntdUI.Input>(detail, "Description").Lines.Length >= 5);
        Assert.Contains("Bob#456", Assert.Single(((Array)Find<AntdUI.Table>(detail, "Participants").DataSource!).Cast<object>().Select(row => row.ToString())));
        Assert.False(Find<AntdUI.Button>(detail, "Join").Enabled);
        foreach (string theme in new[] { "Dark", "Light" })
        {
            UiTheme.SetMode(theme); UiTheme.Apply(dialog); UiTheme.Apply(detail);
            Assert.True(theme == "Dark" ? dialog.BackColor.GetBrightness() < .2f : dialog.BackColor.GetBrightness() > .8f);
        }
        string? preview = Environment.GetEnvironmentVariable("LOL_RECRUITMENT_PREVIEW_PATH");
        if (!string.IsNullOrWhiteSpace(preview))
        {
            UiTheme.SetMode("Dark"); UiTheme.Apply(detail);
            using var bitmap = new Bitmap(detail.Width, detail.Height);
            detail.DrawToBitmap(bitmap, new Rectangle(Point.Empty, detail.Size));
            bitmap.Save(preview, System.Drawing.Imaging.ImageFormat.Png);
        }
        UiTheme.SetMode("System");
    });

    private sealed class LocalRequests : ILcuRequestSender
    {
        public Dictionary<string, string> Responses { get; } = [];
        public Task<string?> GetStringAsync(string endpoint, CancellationToken cancellationToken = default) => Task.FromResult(Responses.GetValueOrDefault(endpoint));
        public Task<byte[]?> GetBytesAsync(string endpoint, CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>(null);
        public Task<bool> PostAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> PutAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> PatchAsync(string endpoint, string jsonBody, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> DeleteAsync(string endpoint, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
