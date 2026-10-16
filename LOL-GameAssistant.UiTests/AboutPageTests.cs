using System.Reflection;
using LOL_GameAssistant.Application.ApplicationInfo;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.ApplicationInfo;
using LOL_GameAssistant.Helper;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class AboutPageTests
{
    [Fact]
    public void AboutPageFillsMainWindowGridAfterNavigatingFromHome() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var host = new AntdUI.Window { ClientSize = new Size(1500, 850), ShowInTaskbar = false };
        AntdWindowChrome.Configure(host, fitWorkingArea: false);
        var root = new AntdUI.GridPanel { Dock = DockStyle.Fill, Span = "100%; 100%;-95% 5%" };
        var tabs = new AntdUI.Tabs { Dock = DockStyle.Fill };
        root.Controls.Add(tabs);
        root.SetIndex(tabs, 1);
        var status = new Panel();
        root.Controls.Add(status);
        root.SetIndex(status, 2);
        host.Controls.Add(root);
        var home = new AntdUI.TabPage { Text = "首页" };
        var aboutTab = new AntdUI.TabPage { Text = "关于" };
        // Use the production designer's container type. A GridPanel with an empty
        // Span ignores Dock.Fill and leaves the deferred page at 150 x 150.
        Type containerType = typeof(GameMain).GetField("tab4_grid1", BindingFlags.Instance | BindingFlags.NonPublic)!.FieldType;
        var grid = (Control)Activator.CreateInstance(containerType)!;
        grid.Dock = DockStyle.Fill;
        AboutForm? about = null;
        var deferred = new DeferredPageHost(() => about = new AboutForm(new NoUpdateService()));
        grid.Controls.Add(deferred);
        aboutTab.Controls.Add(grid);
        tabs.Controls.Add(home);
        tabs.Pages.Add(home);
        tabs.Controls.Add(aboutTab);
        tabs.Pages.Add(aboutTab);
        host.Show();
        System.Windows.Forms.Application.DoEvents();
        Assert.Null(about);
        tabs.SelectedIndex = 1;
        SecondPhaseRegressionTests.PumpUntil(() => about is { IsHandleCreated: true });
        System.Windows.Forms.Application.DoEvents();
        Assert.True(deferred.Width > 1000 && deferred.Height > 600, $"Grid: {grid.Bounds}; deferred: {deferred.Bounds}; about: {about!.Bounds}");
        Assert.True(about!.Width > 1000 && about.Height > 600);
        var features = Children(about).OfType<Panel>().Single(control => control.Name == "aboutFeatures");
        Assert.True(features.Height > 300 && features.Width > 1000);
        var footer = Children(about).Single(control => control.Name == "aboutFooter");
        Assert.True(footer.Top > 500);
        foreach (int width in new[] { 760, 1500 })
        {
            host.ClientSize = new Size(width, 850);
            tabs.SelectedIndex = 0;
            tabs.SelectedIndex = 1;
            System.Windows.Forms.Application.DoEvents();
            Assert.Equal(grid.ClientRectangle, deferred.Bounds);
            Assert.Equal(deferred.ClientRectangle, about.Bounds);
            Assert.True(features.Height > 300);
        }
        string? preview = Environment.GetEnvironmentVariable("ABOUT_MAIN_PREVIEW");
        if (preview != null)
        {
            using var bitmap = new Bitmap(host.Width, host.Height);
            host.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(preview);
        }
    });

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void DeferredPageReflowsBothTabsAfterResizeAndLanguageSwitch(string language) => MatchListScrollingTests.OnUiThread(() =>
    {
        bool wasEnglish = UiLanguage.IsEnglish;
        string originalTheme = UiTheme.Mode;
        try
        {
            UiTheme.SetMode("Dark");
            UiLanguage.SetMode(language);
            AboutForm? about = null;
            using var host = new Form { ClientSize = new Size(500, 620), ShowInTaskbar = false };
            host.Controls.Add(new DeferredPageHost(() => about = new AboutForm(new NoUpdateService())));
            host.Show();
            SecondPhaseRegressionTests.PumpUntil(() => about is { IsHandleCreated: true });
            UiLanguage.Refresh();
            about!.ApplyLanguage();
            foreach (int width in new[] { 500, 1100, 760, 1500, 500 })
            {
                host.ClientSize = new Size(width, 620);
                System.Windows.Forms.Application.DoEvents();
                var tabs = Children(about).OfType<AntdUI.Segmented>().Single();
                for (int tab = 0; tab < 2; tab++)
                {
                    tabs.SelectIndex = tab;
                    System.Windows.Forms.Application.DoEvents();
                    string name = tab == 0 ? "aboutFeatures" : "aboutWebsites";
                    var page = Children(about).OfType<Panel>().Single(control => control.Name == name);
                    page.AutoScrollPosition = Point.Empty;
                    var list = page.Controls.OfType<TableLayoutPanel>().Single();
                    Assert.Equal(width >= 1100 ? 2 : 1, list.ColumnCount);
                    Assert.False(page.HorizontalScroll.Visible);
                    var cards = list.Controls.OfType<TableLayoutPanel>().ToArray();
                    Assert.All(cards, card =>
                    {
                        Assert.True(list.ClientRectangle.Contains(card.Bounds), $"{language}/{width}/{name}: {card.Bounds} outside {list.ClientRectangle}");
                        Assert.All(card.Controls.OfType<AntdUI.Label>(), label =>
                        {
                            Assert.True(card.ClientRectangle.Contains(label.Bounds));
                            Assert.True(label.Height >= label.GetPreferredSize(new Size(label.Width, 0)).Height);
                            Size text = TextRenderer.MeasureText(label.Text, label.Font,
                                new Size(label.Width, int.MaxValue),
                                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl);
                            Assert.True(label.Height >= text.Height, $"Text clipped: {label.Text}");
                        });
                    });
                    for (int index = 1; index < cards.Length; index++)
                        Assert.False(cards[index - 1].Bounds.IntersectsWith(cards[index].Bounds));
                    var footer = Children(about).Single(control => control.Name == "aboutFooter");
                    Assert.All(footer.Controls.Cast<Control>(), control => Assert.True(footer.ClientRectangle.Contains(control.Bounds)));
                    Assert.True(about.ClientRectangle.Contains(footer.Bounds));
                    Assert.True(page.RectangleToScreen(page.ClientRectangle).Bottom <= footer.Top + about.PointToScreen(Point.Empty).Y);
                    string? previews = Environment.GetEnvironmentVariable("ABOUT_LAYOUT_PREVIEWS");
                    if (previews != null && width == 1100)
                    {
                        Directory.CreateDirectory(previews);
                        using var bitmap = new Bitmap(host.Width, host.Height);
                        host.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        bitmap.Save(Path.Combine(previews, $"about-{language}-{name}.png"));
                    }
                }
            }
        }
        finally
        {
            UiTheme.SetMode(originalTheme);
            UiLanguage.SetMode(wasEnglish ? "en-US" : "zh-CN");
        }
    });

    [Theory]
    [InlineData(760, "Light")]
    [InlineData(1100, "Dark")]
    public void BothTabsKeepFooterAndLinksWork(int width, string mode) => MatchListScrollingTests.OnUiThread(() =>
    {
        var opened = new List<string>();
        using var about = new AboutForm(new NoUpdateService(), opened.Add) { Dock = DockStyle.Fill };
        using var host = new Form { ClientSize = new Size(width, 680), ShowInTaskbar = false };
        string originalMode = UiTheme.Mode;
        try
        {
            UiTheme.SetMode(mode);
            host.Controls.Add(about);
            UiTheme.Apply(host);
            host.Show();
            var tabs = Children(about).OfType<AntdUI.Segmented>().Single();
            Assert.Equal(2, tabs.Items.Count);
            Assert.True(Children(about).OfType<AntdUI.Label>().Count(label => label.Text == "一键符文与装备") == 1);
            var footer = Children(about).Single(control => control.Name == "aboutFooter");
            for (int index = 0; index < 2; index++)
            {
                tabs.SelectIndex = index;
                System.Windows.Forms.Application.DoEvents();
                Assert.True(footer.Visible);
                Assert.True(about.ClientRectangle.Contains(footer.Bounds));
                Assert.Equal(4, footer.Controls.OfType<AntdUI.Button>().Count());
                foreach (Panel page in Children(about).OfType<Panel>().Where(page => page.Visible && page.Name.StartsWith("about")))
                    Assert.False(page.HorizontalScroll.Visible);
                foreach (AntdUI.Label label in Children(about).OfType<AntdUI.Label>().Where(label => label.Visible && label.Parent is TableLayoutPanel))
                    Assert.True(label.Parent!.ClientRectangle.Contains(label.Bounds));
            }
            foreach (AntdUI.Button button in Children(about).OfType<AntdUI.Button>().Where(button => button.Tag is string)) Click(button);
            Assert.Contains("https://www.op.gg/", opened);
            Assert.Equal(6, opened.Count);
            Click(footer.Controls.OfType<AntdUI.Button>().Single(button => button.Text == "开源项目"));
            Click(footer.Controls.OfType<AntdUI.Button>().Single(button => button.Text == "给项目点个赞"));
            Assert.Equal(8, opened.Count);
            Assert.All(opened.TakeLast(2), url => Assert.Equal("https://github.com/shecong/LOL-GameAssistant", url));
            Click(footer.Controls.OfType<AntdUI.Button>().Single(button => button.Name == "JoinQqGroup"));
            Assert.Contains("uin=1067459719", opened.Last());
            Assert.StartsWith("mqqapi://", opened.Last());
            string? preview = Environment.GetEnvironmentVariable("ABOUT_PAGE_PREVIEW");
            if (width == 1100 && !string.IsNullOrEmpty(preview))
            {
                using var bitmap = new Bitmap(host.Width, host.Height);
                host.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(preview);
            }
        }
        finally { UiTheme.SetMode(originalMode); }
    });

    private static void Click(Control control) => control.GetType().GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!
        .Invoke(control, [EventArgs.Empty]);
    private static IEnumerable<Control> Children(Control root) => root.Controls.Cast<Control>()
        .SelectMany(child => new[] { child }.Concat(Children(child)));
    private sealed class NoUpdateService : IUpdateReleaseService
    {
        public Task<UpdateRelease?> GetLatestAsync(CancellationToken cancellationToken = default) => Task.FromResult<UpdateRelease?>(null);
    }
}
