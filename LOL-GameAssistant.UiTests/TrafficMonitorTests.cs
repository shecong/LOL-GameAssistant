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
        var grid = Children(page).OfType<AntdUI.Table>().Single();
        var rows = Assert.IsAssignableFrom<Array>(grid.DataSource);
        Assert.Equal(NetworkTrafficService.Modules.Count, rows.Length);
        int aiIndex = NetworkTrafficService.Modules.ToList().FindIndex(m => m.Module == NetworkModule.Ai);
        var cell = grid.CellRectangle(aiIndex + 1, 0);
        var click = new MouseEventArgs(MouseButtons.Left, 1, cell.X + cell.Width / 2, cell.Y + cell.Height / 2, 0);
        foreach (string method in new[] { "OnMouseDown", "OnMouseUp" })
            typeof(AntdUI.Table).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(grid, [click]);
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
            Assert.True(grid.Height > 450);
            Assert.IsType<AntdUI.ColumnSwitch>(grid.Columns[0]);
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
