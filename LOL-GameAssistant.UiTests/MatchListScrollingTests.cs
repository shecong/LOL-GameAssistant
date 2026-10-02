using System.Runtime.ExceptionServices;
using LOL_GameAssistant.Helper;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class MatchListScrollingTests
{
    [Fact]
    public void RepeatedLayoutWhileScrolled_DoesNotGrowRangeOrMoveRecords() => OnUiThread(() =>
    {
        using var form = CreateForm(out var panel, out var rows);
        panel.AutoScrollPosition = new Point(0, 100000);
        int maximum = panel.VerticalScroll.Maximum;
        int offset = panel.AutoScrollPosition.Y;
        int lastTop = rows[^1].Top;
        for (int i = 0; i < 30; i++) MatchListScrolling.LayoutRows(panel, rows, 10, 8);
        Assert.Equal(maximum, panel.VerticalScroll.Maximum);
        Assert.Equal(offset, panel.AutoScrollPosition.Y);
        Assert.Equal(lastTop, rows[^1].Top);
        Assert.False(panel.HorizontalScroll.Visible);
    });

    [Fact]
    public void DetailGrowsAboveViewport_PreservesVisibleRecord() => OnUiThread(() =>
    {
        using var form = CreateForm(out var panel, out var rows);
        panel.AutoScrollPosition = new Point(0, 400);
        Control anchor = rows.First(row => row.Top <= 0 && row.Bottom > 0);
        int top = anchor.Top;
        rows[0].Height += 250;
        MatchListScrolling.LayoutRows(panel, rows, 10, 8);
        Assert.Equal(top, anchor.Top);
        Assert.False(panel.HorizontalScroll.Visible);
    });

    [Fact]
    public void WheelAtBothEnds_IsClampedAndConsumed() => OnUiThread(() =>
    {
        using var form = CreateForm(out var panel, out var rows);
        int remainder = 0;
        for (int i = 0; i < 100; i++)
        {
            var wheel = new HandledMouseEventArgs(MouseButtons.None, 0, 0, 0, -120);
            MatchListScrolling.ScrollWheel(panel, wheel, ref remainder);
            Assert.True(wheel.Handled);
        }
        Assert.Equal(panel.VerticalScroll.Maximum - panel.VerticalScroll.LargeChange + 1,
            -panel.AutoScrollPosition.Y);
        Assert.InRange(rows[^1].Bottom, panel.ClientSize.Height - 30, panel.ClientSize.Height);
        for (int i = 0; i < 100; i++)
            MatchListScrolling.ScrollWheel(panel, new HandledMouseEventArgs(MouseButtons.None, 0, 0, 0, 120), ref remainder);
        Assert.Equal(0, panel.AutoScrollPosition.Y);
    });

    [Fact]
    public void ShrinkingContentAndClearing_DoesNotLeaveBlankScrollRange() => OnUiThread(() =>
    {
        using var form = CreateForm(out var panel, out var rows);
        panel.AutoScrollPosition = new Point(0, 100000);
        foreach (Control row in rows) row.Height = 8;
        MatchListScrolling.LayoutRows(panel, rows, 10, 2);
        Assert.Equal(0, panel.AutoScrollPosition.Y);
        Assert.False(panel.VerticalScroll.Visible);
        ControlLifetime.ClearAndDispose(panel);
        Assert.Equal(Size.Empty, panel.AutoScrollMinSize);
        Assert.Equal(Point.Empty, panel.AutoScrollPosition);
    });

    [Fact]
    public void NestedList_ConsumesWheelAtBoundaryButForwardsWhenAllRowsFit() => OnUiThread(() =>
    {
        using var form = CreateForm(out var outer, out var rows);
        using var inner = new MatchScrollPanel { AutoScroll = true, Bounds = new Rectangle(0, 0, 300, 90) };
        rows[0].Controls.Add(inner);
        inner.Controls.Add(new Panel { Bounds = new Rectangle(0, 0, 200, 500) });
        inner.PerformLayout();
        inner.AutoScrollPosition = new Point(0, 100000);
        int remainder = 0;
        MatchListScrolling.ScrollWheel(inner,
            new HandledMouseEventArgs(MouseButtons.None, 0, 0, 0, -120), ref remainder);
        Assert.Equal(0, outer.AutoScrollPosition.Y);

        ControlLifetime.ClearAndDispose(inner);
        inner.Controls.Add(new Panel { Bounds = new Rectangle(0, 0, 200, 20) });
        inner.PerformLayout();
        MatchListScrolling.ScrollWheel(inner,
            new HandledMouseEventArgs(MouseButtons.None, 0, 0, 0, -120), ref remainder);
        Assert.True(outer.AutoScrollPosition.Y < 0);
    });

    private static Form CreateForm(out MatchScrollPanel panel, out Control[] rows)
    {
        var form = new Form { ClientSize = new Size(500, 300), ShowInTaskbar = false, Opacity = 0 };
        panel = new MatchScrollPanel { Dock = DockStyle.Fill, AutoScroll = true };
        form.Controls.Add(panel);
        rows = Enumerable.Range(0, 10).Select(i => (Control)new Panel
        {
            Height = 100, Width = 400, Location = new Point(10, 10 + i * 108)
        }).ToArray();
        panel.Controls.AddRange(rows);
        form.Show();
        MatchListScrolling.LayoutRows(panel, rows, 10, 8);
        return form;
    }

    internal static void OnUiThread(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
