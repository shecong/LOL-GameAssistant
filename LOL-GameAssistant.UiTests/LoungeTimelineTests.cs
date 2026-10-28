using System.Reflection;
using LOL.Lounge;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.Lounge;
using Xunit;
using static LOL_GameAssistant.UiTests.SecondPhaseRegressionTests;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class LoungeTimelineTests
{
    [Theory]
    [InlineData(1100, false)]
    [InlineData(760, false)]
    [InlineData(1100, true)]
    [InlineData(760, true)]
    public void CompactPresentationFitsThemesAndNarrowWindows(int width, bool dark) => MatchListScrollingTests.OnUiThread(() =>
    {
        string previous = UiTheme.Mode; bool previousAntd = AntdUI.Config.IsDark;
        try
        {
            UiTheme.SetMode(dark ? "Dark" : "Light"); AntdUI.Config.IsDark = dark;
            using var client = Client();
            typeof(LoungeClient).GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(client, client.Settings with { ServerUrl = "http://127.0.0.1:5000" });
            typeof(LoungeClient).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(client, "已连接 · 测试数据预览");
            typeof(LoungeClient).GetMethod("UpdateMembers", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(client, [new LoungeMember[]
            {
                new("member-a", "今晚一起开黑#12345", "艾欧尼亚", "大厅中", "单双排 黄金Ⅰ 93点"),
                new("member-b", "名字很长的队友也不会撑开列表#67890", "未知大区（TENCENT）", "选人中", "单双排 铂金Ⅳ 58点"),
                new("member-c", "韩服队友#KR1", "韩服", "在线", "单双排 黄金Ⅱ 42点")
            }]);
            for (int i = 1; i <= 8; i++) Receive(client, Message(i) with { SenderId = i % 2 == 0 ? "member-a" : "member-b", Name = i % 2 == 0 ? "今晚一起开黑#12345" : "队友#67890", Text = i % 2 == 0 ? "有人一起打大乱斗吗？缺一位，轻松娱乐。" : "我来，等这场结束就进大厅。" });
            using var page = new LoungeForm(client); using var host = new Form { ClientSize = new(width, 760) }; host.Controls.Add(page); host.Show(); UiTheme.Apply(page); Refresh(page); System.Windows.Forms.Application.DoEvents();
            var history = Field<AntdUI.Chat.ChatList>(page, "_history"); var members = Field<AntdUI.Table>(page, "_members");
            Assert.True(history.Height > 400); Assert.Equal(3, ((Array)members.DataSource!).Length); Assert.True(members.Width >= 240);
            var serverFilter = Field<AntdUI.Select>(page, "_memberServerFilter");
            Assert.True(serverFilter.Visible && serverFilter.Height >= 28);
            Assert.True(serverFilter.Bottom <= members.Top);
            var filter = Field<AntdUI.Select>(page, "_messageFilter");
            Assert.True(filter.Height >= 28);
            var regionFilter = Field<AntdUI.Select>(page, "_regionFilter");
            Assert.True(regionFilter.Visible && regionFilter.Height >= 28);
            Assert.True(regionFilter.Left >= filter.Right);
            Assert.True(regionFilter.Right <= regionFilter.Parent!.ClientSize.Width);
            filter.SelectedIndex = 2;
            Assert.DoesNotContain("有人一起打大乱斗", ChatViewAssertions.Text(history));
            filter.SelectedIndex = 0;
            Assert.Contains("有人一起打大乱斗", ChatViewAssertions.Text(history));
            var notice = Field<AntdUI.Label>(page, "_onlineNotice");
            Assert.True(notice.Width > 150, $"实时提示可用宽度: {notice.Width}");
            Assert.True(notice.Right <= notice.Parent!.ClientSize.Width);
            string? previews = Environment.GetEnvironmentVariable("LOL_LOUNGE_PREVIEW_DIRECTORY");
            if (previews != null) { Directory.CreateDirectory(previews); using var image = new Bitmap(page.Width, page.Height); page.DrawToBitmap(image, page.ClientRectangle); image.Save(Path.Combine(previews, $"lounge-{width}-{(dark ? "dark" : "light")}.png")); }
            host.Close();
        }
        finally { AntdUI.Config.IsDark = previousAntd; UiTheme.SetMode(previous); }
    });

    private static LoungeClient Client() => new(new LoungeLcuReader(Service<ILcuRequestSender>((_, _) => Task.FromResult<string?>(null))));
    private static void Receive(LoungeClient client, LoungeMessage message) => typeof(LoungeClient).GetMethod("Receive", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(client, [message, false]);
    private static void Refresh(LoungeForm page) => typeof(LoungeForm).GetMethod("RefreshView", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
    private static Task Older(LoungeForm page) => (Task)typeof(LoungeForm).GetMethod("LoadOlderMessagesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
    private static LoungeMessage Message(int id) => new(id, "", "sender", "测试玩家#123", "艾欧尼亚", "chat", $"消息内容 {id:D4}", DateTimeOffset.Now.AddSeconds(id));

    [Fact]
    public void InitialWindowIsLatestFortyAndOlderBrowsingIsBounded() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var client = Client(); for (int i = 1; i <= 240; i++) Receive(client, Message(i));
        using var page = new LoungeForm(client); using var host = new Form { ClientSize = new(1100, 760) }; host.Controls.Add(page); host.Show();
        var window = Field<SortedDictionary<long, LoungeMessage>>(page, "_window");
        Assert.Equal(40, window.Count); Assert.Equal(201, window.Keys.First());
        for (int i = 0; i < 5; i++) Pump(Older(page));
        Assert.Equal(200, window.Count); Assert.Equal(1, window.Keys.First()); Assert.Equal(200, window.Keys.Last());
        Assert.Equal(0, client.UnreadCount);
        Receive(client, Message(241)); Refresh(page);
        Assert.DoesNotContain(241L, window.Keys); Assert.True(Field<AntdUI.Button>(page, "_newMessages").Visible);
        Field<AntdUI.Button>(page, "_newMessages").PerformClick();
        Assert.Equal(40, window.Count); Assert.Equal(241, window.Keys.Last()); Assert.False(Field<AntdUI.Button>(page, "_newMessages").Visible);
        host.Close();
    });

    [Fact]
    public void PrependingPreservesVisibleMessageAndNewMessagesDoNotPullReaderDown() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var client = Client(); for (int i = 1; i <= 100; i++) Receive(client, Message(i));
        using var page = new LoungeForm(client); using var host = new Form { ClientSize = new(1100, 700) }; host.Controls.Add(page); host.Show(); System.Windows.Forms.Application.DoEvents();
        var history = Field<AntdUI.Chat.ChatList>(page, "_history"); history.ScrollBar.ValueY = 0;
        int oldY = ChatViewAssertions.MessageY(history, 61);
        Pump(Older(page));
        int newY = ChatViewAssertions.MessageY(history, 61);
        Assert.InRange(Math.Abs(newY - oldY), 0, history.Font.Height * 2);
        string oldText = ChatViewAssertions.Text(history); Receive(client, Message(101)); Refresh(page);
        Assert.Equal(oldText, ChatViewAssertions.Text(history)); Assert.True(Field<AntdUI.Button>(page, "_newMessages").Visible);
        host.Close();
    });

    [Fact]
    public void OnlineEventsAreAvailableOnlyInTheirFilterWithoutChatOrUnread() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var client = Client(); var now = DateTimeOffset.Now;
        Receive(client, new(1, "", "sender", "小明#123", "未知大区（TENCENT）", "online", "重复的欢迎来到休息室", now));
        Receive(client, new(2, "", "sender", "小明#123", "未知大区（TENCENT）", "online", "重复的欢迎来到休息室", now.AddSeconds(30)));
        Receive(client, new(3, "", "other", "小红#456", "艾欧尼亚", "chat", "一起开黑吗？", now.AddMinutes(1)));
        Receive(client, new(4, "", "sender", "切换账号#789", "艾欧尼亚", "online", "上线消息", now.AddMinutes(2)));
        using var page = new LoungeForm(client); using var host = new Form { ClientSize = new(1100, 700) }; host.Controls.Add(page); host.Show();
        var history = Field<AntdUI.Chat.ChatList>(page, "_history");
        Assert.Single(client.Messages);
        Assert.DoesNotContain("加入休息室", ChatViewAssertions.Text(history));
        var receiveOnline = typeof(LoungeClient).GetMethod("ReceiveOnline", BindingFlags.Instance | BindingFlags.NonPublic)!;
        receiveOnline.Invoke(client, [new LoungeOnlineNotice("小明#123", "未知大区（TENCENT）")]);
        Refresh(page);
        var banner = Field<AntdUI.Label>(page, "_onlineNotice");
        Assert.Empty(banner.Text ?? "");
        Assert.DoesNotContain("加入休息室", ChatViewAssertions.Text(history));
        var filter = Field<AntdUI.Select>(page, "_messageFilter");
        Assert.Contains("上线通知", filter.Items.Cast<object>());
        filter.SelectedIndex = 4;
        Assert.Contains("小明#123 加入休息室", banner.Text);
        Assert.Contains("地区待确认", banner.Text); Assert.DoesNotContain("TENCENT", banner.Text);
        Assert.Contains("小明#123 加入休息室", ChatViewAssertions.Text(history));
        Assert.DoesNotContain("一起开黑吗", ChatViewAssertions.Text(history));
        Assert.False(Field<AntdUI.Button>(page, "_older").Visible);
        receiveOnline.Invoke(client, [new LoungeOnlineNotice("切换账号#789", "艾欧尼亚")]);
        Refresh(page);
        Assert.Contains("切换账号#789", banner.Text); Assert.DoesNotContain("小明", banner.Text);
        Assert.Single(client.Messages); Assert.Equal(0, client.UnreadCount);
        Assert.Contains("切换账号#789 加入休息室", ChatViewAssertions.Text(history));
        typeof(LoungeClient).GetField("_onlineNoticeUntil", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(client, DateTimeOffset.UtcNow.AddSeconds(-1));
        Refresh(page);
        Assert.Empty(banner.Text ?? ""); Assert.Null(client.OnlineNotice);
        Assert.Contains("小明#123 加入休息室", ChatViewAssertions.Text(history));
        filter.SelectedIndex = 0;
        Assert.DoesNotContain("加入休息室", ChatViewAssertions.Text(history)); Assert.Contains("一起开黑吗", ChatViewAssertions.Text(history));
        filter.SelectedIndex = 1;
        Assert.DoesNotContain("加入休息室", ChatViewAssertions.Text(history)); Assert.Contains("一起开黑吗", ChatViewAssertions.Text(history));
        host.Close();
    });

    [Fact]
    public void RegionFilterCombinesMessageTypesMembersAndOnlineNotices() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var client = Client();
        typeof(LoungeClient).GetMethod("UpdateMembers", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(client, [new LoungeMember[]
        {
            new("merged", "联盟玩家", "未知大区（NJ100）", "在线"), new("ionia", "艾欧玩家", "艾欧尼亚", "在线")
        }]);
        Receive(client, Message(1) with { Region = "未知大区（NJ100）", Text = "联盟聊天" });
        Receive(client, Message(2) with { Text = "艾欧聊天" });
        Receive(client, Message(3) with { Region = "TENCENT_NJ100", Kind = "victory", Text = "联盟胜利" });
        Receive(client, Message(4) with { Kind = "recruit", Text = LoungeContent.Serialize(new LoungeRecruitment("联盟一区", 420, "不限段位", "辅助", 1, "联盟招募")) });
        Receive(client, Message(5) with { Region = "NJ100", Kind = "online", Name = "联盟上线" });
        using var page = new LoungeForm(client); using var host = new Form { ClientSize = new(1100, 760) };
        host.Controls.Add(page); host.Show();
        var region = Field<AntdUI.Select>(page, "_regionFilter");
        Assert.Contains("全部区服", region.Items.Cast<object>());
        Assert.Contains("当前区服", region.Items.Cast<object>());
        Assert.Contains("联盟五区", region.Items.Cast<object>());
        Assert.Contains("日服", region.Items.Cast<object>());
        region.SelectedValue = "联盟一区";
        var history = Field<AntdUI.Chat.ChatList>(page, "_history");
        Assert.Contains("联盟聊天", ChatViewAssertions.Text(history));
        Assert.DoesNotContain("艾欧聊天", ChatViewAssertions.Text(history));
        var members = Field<AntdUI.Table>(page, "_members");
        Assert.Contains("联盟玩家", Assert.Single(((Array)members.DataSource!).Cast<object>()).ToString());
        Assert.Contains("1/2", Field<AntdUI.Label>(page, "_memberCount").Text);
        var type = Field<AntdUI.Select>(page, "_messageFilter");
        type.SelectedIndex = 2;
        Assert.Contains("联盟胜利", ChatViewAssertions.Text(history));
        Assert.DoesNotContain("联盟聊天", ChatViewAssertions.Text(history));
        type.SelectedIndex = 3;
        Assert.Contains("联盟招募", ChatViewAssertions.Text(history));
        type.SelectedIndex = 4;
        var receiveOnline = typeof(LoungeClient).GetMethod("ReceiveOnline", BindingFlags.Instance | BindingFlags.NonPublic)!;
        receiveOnline.Invoke(client, [new LoungeOnlineNotice("艾欧上线", "艾欧尼亚")]); Refresh(page);
        Assert.Empty(Field<AntdUI.Label>(page, "_onlineNotice").Text ?? "");
        Assert.Contains("联盟上线", ChatViewAssertions.Text(history));
        Assert.DoesNotContain("艾欧上线", ChatViewAssertions.Text(history));
        receiveOnline.Invoke(client, [new LoungeOnlineNotice("同区好友上线", "NJ100")]); Refresh(page);
        Assert.Contains("同区好友上线", Field<AntdUI.Label>(page, "_onlineNotice").Text);
        region.SelectedIndex = 0; type.SelectedIndex = 0;
        Assert.Contains("艾欧聊天", ChatViewAssertions.Text(history));
        Assert.Equal(2, ((Array)members.DataSource!).Length);
        Assert.Equal(4, client.Messages.Length);
    });

    [Fact]
    public void MemberServerFilterDistinguishesChinaOtherAndPendingWithoutChangingChat() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var client = Client();
        typeof(LoungeClient).GetMethod("UpdateMembers", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(client, [new LoungeMember[]
        {
            new("ionia", "国服队友#123", "艾欧尼亚", "在线"),
            new("merged", "合区队友#456", "未知大区（NJ100）", "在线"),
            new("kr", "韩服队友#KR1", "韩服", "在线", "单双排 黄金Ⅱ 42点"),
            new("na", "北美队友#NA1", "NA1", "在线"),
            new("future", "未知队友#789", "未知大区（FUTURE）", "在线"),
            new("pbe", "测试服队友#PBE", "测试服（PBE）", "在线")
        }]);
        Receive(client, Message(1) with { Text = "国服消息" });
        Receive(client, Message(2) with { Region = "韩服", Name = "韩服队友#KR1", Text = "海外消息" });
        using var page = new LoungeForm(client); using var host = new Form { ClientSize = new(1100, 760) };
        host.Controls.Add(page); host.Show();
        var table = Field<AntdUI.Table>(page, "_members");
        string Rows() => string.Join("\n", ((Array)table.DataSource!).Cast<object>());
        Assert.Contains("国服 · 艾欧尼亚", Rows());
        Assert.Contains("国服 · 联盟一区", Rows());
        Assert.Contains("其他服务器 · 韩服", Rows());
        Assert.Contains("其他服务器 · 北美", Rows());
        Assert.Contains("服务器待确认 · 地区待确认", Rows());
        var server = Field<AntdUI.Select>(page, "_memberServerFilter");
        server.SelectedValue = "国服";
        Assert.Equal(2, ((Array)table.DataSource!).Length);
        Assert.DoesNotContain("韩服队友", Rows());
        Assert.Contains("2/6", Field<AntdUI.Label>(page, "_memberCount").Text);
        var history = Field<AntdUI.Chat.ChatList>(page, "_history");
        Assert.Contains("海外消息", ChatViewAssertions.Text(history));
        Receive(client, Message(3) with { Region = "韩服", Name = "韩服队友#KR1", Kind = "victory", Text = "海外战绩" });
        Refresh(page);
        Assert.Contains("单双排 黄金Ⅱ 42点", history.Items.OfType<AntdUI.Chat.TextChatItem>().Last().Name);
        server.SelectedValue = "其他服务器";
        Assert.Equal(2, ((Array)table.DataSource!).Length);
        Assert.DoesNotContain("国服队友", Rows());
        table.SelectedIndex = 0;
        Assert.Equal("韩服队友#KR1", LoungeForm.MemberCopyText(Field<LoungeMember[]>(page, "_visibleMembers")[table.SelectedIndex], "完整游戏ID"));
        var region = Field<AntdUI.Select>(page, "_regionFilter"); region.SelectedValue = "联盟一区";
        Assert.Empty(((Array)table.DataSource!).Cast<object>());
        server.SelectedValue = "国服";
        Assert.Contains("合区队友", Assert.Single(((Array)table.DataSource!).Cast<object>()).ToString());
        region.SelectedIndex = 0; server.SelectedValue = "服务器待确认";
        Assert.Equal(2, ((Array)table.DataSource!).Length);
        Assert.Contains("测试服队友", Rows());
        Assert.DoesNotContain("其他服务器", Rows());
        server.SelectedIndex = 0;
        Assert.Equal(6, ((Array)table.DataSource!).Length);
        Assert.Equal(6, client.Members.Length);
        Assert.Equal(3, client.Messages.Length);
    });

    [Fact]
    public void CurrentRegionRefreshesAfterLoginAndAccountChangesWithoutReadingHiddenMessages() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var client = Client();
        Receive(client, Message(1) with { Text = "艾欧消息" });
        Receive(client, Message(2) with { Region = "联盟一区", Text = "联盟消息" });
        using var page = new LoungeForm(client); using var host = new Form { ClientSize = new(1100, 760) };
        host.Controls.Add(page); host.Show();
        var region = Field<AntdUI.Select>(page, "_regionFilter"); region.SelectedIndex = 1;
        var history = Field<AntdUI.Chat.ChatList>(page, "_history");
        Assert.Contains("当前区服尚未确认", ChatViewAssertions.Text(history));
        void Presence(string puuid, string platform, string name) => typeof(LoungeClient)
            .GetField("_presence", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(client, new LoungePresence(puuid, "自己", platform, name, "在线"));
        Presence("alice", "HN1", "艾欧尼亚"); Refresh(page);
        Assert.Contains("艾欧消息", ChatViewAssertions.Text(history));
        Assert.DoesNotContain("联盟消息", ChatViewAssertions.Text(history));
        Presence("bob", "NJ100", "未知大区（NJ100）"); Refresh(page);
        Assert.Contains("联盟消息", ChatViewAssertions.Text(history));
        Assert.DoesNotContain("艾欧消息", ChatViewAssertions.Text(history));
        typeof(LoungeClient).GetMethod("Receive", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(client, [Message(3) with { SenderId = "other", Text = "其他区服新消息" }, true]);
        host.Activate(); page.Focus(); Refresh(page);
        Assert.Equal(1, client.UnreadCount);
        Assert.DoesNotContain("其他区服新消息", ChatViewAssertions.Text(history));
        Presence("", "", "未知大区"); Refresh(page);
        Assert.Contains("当前区服尚未确认", ChatViewAssertions.Text(history));
    });

    [Fact]
    public void RegionSelectionUsesFortyMatchingMessagesAndCanReturnToAll() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var client = Client();
        for (int i = 1; i <= 120; i++) Receive(client, Message(i) with { Region = i % 2 == 0 ? "联盟四区" : "艾欧尼亚" });
        using var page = new LoungeForm(client); using var host = new Form { ClientSize = new(1100, 760) };
        host.Controls.Add(page); host.Show();
        var region = Field<AntdUI.Select>(page, "_regionFilter"); region.SelectedValue = "联盟四区";
        var window = Field<SortedDictionary<long, LoungeMessage>>(page, "_window");
        Assert.Equal(40, window.Count);
        Assert.Equal(42, window.Keys.First());
        Assert.All(window.Values, message => Assert.Equal("联盟四区", message.Region));
        Receive(client, Message(121)); Refresh(page);
        Assert.Equal(120, window.Keys.Last());
        Field<AntdUI.Button>(page, "_newMessages").PerformClick();
        Assert.Equal(120, window.Keys.Last());
        region.SelectedValue = "日服";
        Assert.Contains("暂无符合条件的消息", ChatViewAssertions.Text(Field<AntdUI.Chat.ChatList>(page, "_history")));
        Assert.True(Field<AntdUI.Button>(page, "_older").Enabled);
        Pump(Older(page));
        Assert.Contains("服务器暂不支持历史分页", Field<AntdUI.Label>(page, "_feedback").Text);
        Assert.True(Field<AntdUI.Button>(page, "_older").Enabled);
        region.SelectedIndex = 0;
        Assert.Equal(40, window.Count);
        Assert.Equal(121, window.Keys.Last());
        Assert.Equal(121, client.Messages.Length);
    });

    [Fact]
    public void OnlineHistoryIsBoundedAndLegacyReplaysDoNotDuplicateOrDisplaceChat()
    {
        using var client = Client();
        Receive(client, Message(1));
        var online = Message(2) with { Kind = "online" };
        Receive(client, online); Receive(client, online);
        Assert.Single(client.OnlineMessages);
        var receiveOnline = typeof(LoungeClient).GetMethod("ReceiveOnline", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (int i = 0; i < 210; i++) receiveOnline.Invoke(client, [new LoungeOnlineNotice($"玩家{i}", "艾欧尼亚")]);
        Assert.Equal(200, client.OnlineMessages.Length);
        Assert.Equal("玩家10", client.OnlineMessages[0].Name);
        Assert.Equal("玩家209", client.OnlineMessages[^1].Name);
        Assert.Single(client.Messages);
        Assert.Equal(0, client.UnreadCount);
    }
}
