namespace LOL_GameAssistant.Helper;

/// <summary>战绩列表使用固定虚拟坐标，重排时保留正在阅读的记录。</summary>
internal static class MatchListScrolling
{
    public static void LayoutRows(Panel panel, IReadOnlyList<Control> rows, int inset, int gap)
    {
        if (panel.IsDisposed || panel.ClientSize.Width <= 0) return;
        int previousOffset = -panel.AutoScrollPosition.Y;
        Control? anchor = rows.FirstOrDefault(row => row.Top <= 0 && row.Bottom > 0)
            ?? rows.FirstOrDefault(row => row.Top >= 0);
        int anchorTop = anchor?.Top ?? 0;
        panel.SuspendLayout();
        try
        {
            // ClientSize 已扣除可见滚动条，不再重复减去滚动条宽度。
            int width = Math.Max(1, panel.ClientSize.Width - inset * 2);
            foreach (Control row in rows) row.Width = width;
            int contentHeight = inset + rows.Sum(row => row.Height + gap);
            panel.AutoScrollMinSize = new Size(0, contentHeight);
            width = Math.Max(1, panel.ClientSize.Width - inset * 2);
            foreach (Control row in rows) row.Width = width;
            contentHeight = inset + rows.Sum(row => row.Height + gap);
            panel.AutoScrollMinSize = new Size(0, contentHeight);

            int anchorY = inset;
            foreach (Control row in rows)
            {
                if (row == anchor) break;
                anchorY += row.Height + gap;
            }
            int targetOffset = previousOffset == 0 || anchor == null ? previousOffset : anchorY - anchorTop;
            targetOffset = Math.Clamp(targetOffset, 0, Math.Max(0, contentHeight - panel.ClientSize.Height));
            panel.AutoScrollPosition = new Point(0, targetOffset);
            int y = inset + panel.AutoScrollPosition.Y;
            foreach (Control row in rows)
            {
                row.Location = new Point(inset, y);
                y += row.Height + gap;
            }
        }
        finally { panel.ResumeLayout(true); }
    }

    public static void ScrollWheel(ScrollableControl panel, MouseEventArgs e, ref int remainder)
    {
        if (e is HandledMouseEventArgs { Handled: true }) return;
        int maximum = panel.VerticalScroll.Visible
            ? Math.Max(0, panel.VerticalScroll.Maximum - panel.VerticalScroll.LargeChange + 1) : 0;
        if (maximum == 0)
        {
            // 卡片内记录已全部显示时，让队伍列表接收滚轮。
            for (Control? parent = panel.Parent; parent != null; parent = parent.Parent)
            {
                if (parent is ScrollableControl { AutoScroll: true, VerticalScroll.Visible: true } outer)
                {
                    ScrollWheel(outer, e, ref remainder);
                    return;
                }
            }
        }
        // 在列表边界消费滚轮，避免继续滚动外层队伍面板。
        if (e is HandledMouseEventArgs handled) handled.Handled = true;
        int lines = SystemInformation.MouseWheelScrollLines;
        if (lines == 0) return;
        int step = lines < 0 ? panel.ClientSize.Height : Math.Min(lines * 24, panel.ClientSize.Height);
        remainder += e.Delta * step;
        int pixels = remainder / SystemInformation.MouseWheelScrollDelta;
        if (pixels == 0) return;
        remainder %= SystemInformation.MouseWheelScrollDelta;
        int next = Math.Clamp(-panel.AutoScrollPosition.Y - pixels, 0, maximum);
        panel.AutoScrollPosition = new Point(0, next);
    }
}

internal class MatchScrollPanel : Panel
{
    private int _wheelRemainder;
    public MatchScrollPanel() => DoubleBuffered = true;
    protected override void OnMouseWheel(MouseEventArgs e) =>
        MatchListScrolling.ScrollWheel(this, e, ref _wheelRemainder);
}

internal class MatchFlowPanel : FlowLayoutPanel
{
    private int _wheelRemainder;
    public MatchFlowPanel() => DoubleBuffered = true;
    protected override void OnMouseWheel(MouseEventArgs e) =>
        MatchListScrolling.ScrollWheel(this, e, ref _wheelRemainder);
}
