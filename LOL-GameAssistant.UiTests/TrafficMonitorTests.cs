using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Helper;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class TrafficMonitorTests
{
    [Fact]
    public void CheckboxAndWeakNetworkModeApplyToTheCorrectModules() => MatchListScrollingTests.OnUiThread(() =>
    {
        var traffic = new NetworkTrafficService();
        using var page = new TrafficMonitorForm(traffic);
        using var host = new Form { ClientSize = new Size(1420, 840), ShowInTaskbar = false, Opacity = 0 };
        host.Controls.Add(page); host.Show();
        System.Windows.Forms.Application.DoEvents();
        var grid = Children(page).OfType<DataGridView>().Single();
        Assert.Equal(NetworkTrafficService.Modules.Count, grid.Rows.Count);
        var ai = grid.Rows.Cast<DataGridViewRow>().Single(r => r.Tag is NetworkModule.Ai);
        ai.Cells[0].Value = false;
        Assert.False(traffic.IsEnabled(NetworkModule.Ai));
        Assert.True(traffic.IsEnabled(NetworkModule.Builds));
        Children(page).OfType<AntdUI.Button>().Single(b => b.Text!.StartsWith("弱网模式")).PerformClick();
        Assert.All(traffic.Snapshot().Where(s => !s.Info.Local), s => Assert.False(s.Enabled));
        Assert.All(traffic.Snapshot().Where(s => s.Info.Local), s => Assert.True(s.Enabled));
        Children(page).OfType<AntdUI.Button>().Single(b => b.Text == "清空本次统计").PerformClick();
        Assert.False(traffic.IsEnabled(NetworkModule.Builds));
        Children(page).OfType<AntdUI.Button>().Single(b => b.Text == "允许所有模块访问").PerformClick();
        Assert.All(traffic.Snapshot(), s => Assert.True(s.Enabled));
        foreach (string mode in new[] { "Light", "Dark" })
        {
            UiTheme.SetMode(mode); UiTheme.Apply(page);
            System.Windows.Forms.Application.DoEvents();
            Assert.True(grid.Height > 500);
            Assert.True(grid.Columns[0].Width >= 72);
            string? directory = Environment.GetEnvironmentVariable("TRAFFIC_UI_PREVIEWS");
            if (directory != null)
            {
                Directory.CreateDirectory(directory);
                using var image = new Bitmap(page.Width, page.Height);
                page.DrawToBitmap(image, page.ClientRectangle);
                image.Save(Path.Combine(directory, "traffic-" + mode + ".png"));
            }
        }
        UiTheme.SetMode("System");
    });

    [Fact]
    public void LogMenuLoadsAndReusesTrafficPage() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var messages = new InfoMsgForm();
        using var logs = new LogToolsForm(messages, () => new Panel());
        using var host = new Form { ClientSize = new Size(1420, 840), ShowInTaskbar = false, Opacity = 0 };
        host.Controls.Add(logs); host.Show();
        var menu = Children(logs).OfType<AntdUI.Segmented>().Single();
        Assert.Equal("流量监控", menu.Items[2].Text);
        Assert.Empty(Children(logs).OfType<TrafficMonitorForm>());
        menu.SelectIndex = 2;
        System.Windows.Forms.Application.DoEvents();
        var page = Assert.Single(Children(logs).OfType<TrafficMonitorForm>());
        Assert.True(page.Visible);
        Assert.False(messages.Visible);
        menu.SelectIndex = 0;
        Assert.False(page.Visible);
        Assert.True(messages.Visible);
        menu.SelectIndex = 2;
        Assert.Same(page, Assert.Single(Children(logs).OfType<TrafficMonitorForm>()));
    });
    private static IEnumerable<Control> Children(Control control) => control.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Children(c)));
}
