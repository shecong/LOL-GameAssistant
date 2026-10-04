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
                Assert.Equal(3, footer.Controls.OfType<AntdUI.Button>().Count());
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
