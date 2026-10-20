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
                new("member-b", "名字很长的队友也不会撑开列表#67890", "未知大区（TENCENT）", "选人中", "单双排 铂金Ⅳ 58点")
            }]);
            for (int i = 1; i <= 8; i++) Receive(client, Message(i) with { SenderId = i % 2 == 0 ? "member-a" : "member-b", Name = i % 2 == 0 ? "今晚一起开黑#12345" : "队友#67890", Text = i % 2 == 0 ? "有人一起打大乱斗吗？缺一位，轻松娱乐。" : "我来，等这场结束就进大厅。" });
            using var page = new LoungeForm(client); using var host = new Form { ClientSize = new(width, 760) }; host.Controls.Add(page); host.Show(); UiTheme.Apply(page); Refresh(page); System.Windows.Forms.Application.DoEvents();
            var history = Field<RichTextBox>(page, "_history"); var members = Field<ListBox>(page, "_members");
            Assert.True(history.Height > 400); Assert.Equal(2, members.Items.Count); Assert.True(members.Width >= 240);
            var notice = Field<Label>(page, "_onlineNotice");
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
        Assert.DoesNotContain(241L, window.Keys); Assert.True(Field<Button>(page, "_newMessages").Visible);
        Field<Button>(page, "_newMessages").PerformClick();
        Assert.Equal(40, window.Count); Assert.Equal(241, window.Keys.Last()); Assert.False(Field<Button>(page, "_newMessages").Visible);
        host.Close();
    });

    [Fact]
    public void PrependingPreservesVisibleMessageAndNewMessagesDoNotPullReaderDown() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var client = Client(); for (int i = 1; i <= 100; i++) Receive(client, Message(i));
        using var page = new LoungeForm(client); using var host = new Form { ClientSize = new(1100, 700) }; host.Controls.Add(page); host.Show(); System.Windows.Forms.Application.DoEvents();
        var history = Field<RichTextBox>(page, "_history"); history.SelectionStart = 0; history.ScrollToCaret();
        int oldY = history.GetPositionFromCharIndex(history.Text.IndexOf("消息内容 0061", StringComparison.Ordinal)).Y;
        Pump(Older(page));
        int newY = history.GetPositionFromCharIndex(history.Text.IndexOf("消息内容 0061", StringComparison.Ordinal)).Y;
        Assert.InRange(Math.Abs(newY - oldY), 0, history.Font.Height * 2);
        string oldText = history.Text; Receive(client, Message(101)); Refresh(page);
        Assert.Equal(oldText, history.Text); Assert.True(Field<Button>(page, "_newMessages").Visible);
        host.Close();
    });

    [Fact]
    public void OnlineEventsExpireWithoutHistoryOrUnreadAndUnknownRegionCodeIsHidden() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var client = Client(); var now = DateTimeOffset.Now;
        Receive(client, new(1, "", "sender", "小明#123", "未知大区（TENCENT）", "online", "重复的欢迎来到休息室", now));
        Receive(client, new(2, "", "sender", "小明#123", "未知大区（TENCENT）", "online", "重复的欢迎来到休息室", now.AddSeconds(30)));
        Receive(client, new(3, "", "other", "小红#456", "艾欧尼亚", "chat", "一起开黑吗？", now.AddMinutes(1)));
        Receive(client, new(4, "", "sender", "切换账号#789", "艾欧尼亚", "online", "上线消息", now.AddMinutes(2)));
        using var page = new LoungeForm(client); using var host = new Form { ClientSize = new(1100, 700) }; host.Controls.Add(page); host.Show();
        var history = Field<RichTextBox>(page, "_history");
        Assert.Single(client.Messages);
        Assert.DoesNotContain("加入休息室", history.Text);
        var receiveOnline = typeof(LoungeClient).GetMethod("ReceiveOnline", BindingFlags.Instance | BindingFlags.NonPublic)!;
        receiveOnline.Invoke(client, [new LoungeOnlineNotice("小明#123", "未知大区（TENCENT）")]);
        Refresh(page);
        var banner = Field<Label>(page, "_onlineNotice");
        Assert.Contains("小明#123 加入休息室", banner.Text);
        Assert.Contains("地区待确认", banner.Text); Assert.DoesNotContain("TENCENT", banner.Text);
        receiveOnline.Invoke(client, [new LoungeOnlineNotice("切换账号#789", "艾欧尼亚")]);
        Refresh(page);
        Assert.Contains("切换账号#789", banner.Text); Assert.DoesNotContain("小明", banner.Text);
        Assert.Single(client.Messages); Assert.Equal(0, client.UnreadCount);
        Assert.DoesNotContain("加入休息室", history.Text);
        typeof(LoungeClient).GetField("_onlineNoticeUntil", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(client, DateTimeOffset.UtcNow.AddSeconds(-1));
        Refresh(page);
        Assert.Empty(banner.Text); Assert.Null(client.OnlineNotice);
        Field<ComboBox>(page, "_messageFilter").SelectedIndex = 1;
        Assert.DoesNotContain("加入休息室", history.Text); Assert.Contains("一起开黑吗", history.Text);
        host.Close();
    });
}
