using System.Reflection;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Teams;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Helper;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class RecentMatchRowHoverTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LeavingRowRestoresTheAppliedThemeIncludingAfterRepeatedChildTransitions(bool dark) =>
        MatchListScrollingTests.OnUiThread(() =>
        {
            using var form = new Form { ClientSize = new Size(500, 200), Opacity = 0, ShowInTaskbar = false };
            var row = new RecentMatchRow(Service<IGameAssetService>(), Service<IPremadeDetectionService>())
            { Bounds = new Rectangle(10, 10, 450, 40) };
            form.Controls.Add(row);
            form.Show();
            var palette = UiTheme.Palette with { IsDark = dark, SurfaceRaised = Color.FromArgb(48, 58, 72) };
            row.ApplyTheme(palette);
            Color normal = row.BackColor;
            if (dark) Assert.Equal(palette.SurfaceRaised, normal);
            Point inside = row.PointToScreen(new Point(20, 20));
            Point outside = row.PointToScreen(new Point(-10, -10));
            for (int i = 0; i < 3; i++)
            {
                row.UpdateHoverState(inside);
                Color hover = Field<Color>(row, "_hoverTo");
                Assert.NotEqual(normal, hover);
                row.BackColor = hover;
                // Moving between labels within the same row must keep the hover state.
                row.UpdateHoverState(row.PointToScreen(new Point(150, 20)));
                Assert.Equal(hover, row.BackColor);
                row.UpdateHoverState(outside);
                Assert.Equal(normal, row.BackColor);
                Assert.False(Field<System.Windows.Forms.Timer>(row, "_hoverTimer").Enabled);
            }
            row.UpdateHoverState(inside);
            row.BackColor = Field<Color>(row, "_hoverTo");
            var changed = palette with { IsDark = true, SurfaceRaised = Color.FromArgb(37, 45, 58) };
            row.ApplyTheme(changed);
            row.UpdateHoverState(outside);
            Assert.Equal(changed.SurfaceRaised, row.BackColor);
        });

    private static T Field<T>(object target, string name) => (T)target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static T Service<T>() where T : class
    {
        T proxy = DispatchProxy.Create<T, ChampSelectCompanionTests.ServiceProxy>();
        ((ChampSelectCompanionTests.ServiceProxy)(object)proxy).Handler = (_, _) => null;
        return proxy;
    }
}
