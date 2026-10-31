using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public partial class SettingForm
{
    /// <summary>AntdUI 2.4.12 provides Menu rather than Anchor. Bind its items to the actual cards.</summary>
    private Control AddAiSectionNavigation(AntdUI.Panel page, TableLayoutPanel content)
    {
        var host = new SettingsDashboard { Name = "aiSettingsWorkspace", Size = page.Size };
        host.SuspendLayout();
        var directory = new SettingsDashboard { Name = "aiSectionDirectory", Dock = DockStyle.Right, Width = 210, Padding = new Padding(8, 12, 8, 8) };
        var menu = new AntdUI.Menu
        {
            Name = "aiSectionNavigation", Dock = DockStyle.Fill, Mode = AntdUI.TMenuMode.Inline,
            Font = new Font(UiMetrics.FontFamily, 10F), AccessibleName = "AI 与推荐功能目录"
        };
        var heading = new AntdUI.Label { Text = "本页功能", Dock = DockStyle.Top, Height = 36,
            Padding = new Padding(12, 0, 0, 0), Font = new Font(UiMetrics.FontFamily, 11F, FontStyle.Bold) };
        directory.Controls.Add(menu); directory.Controls.Add(heading);
        page.Dock = DockStyle.Fill;
        host.Controls.Add(page); host.Controls.Add(directory);
        var sections = content.Controls.Cast<Control>().Where(card => card.Name.EndsWith("Card", StringComparison.Ordinal)).ToArray();
        foreach (var card in sections)
        {
            var rows = card.Controls.OfType<TableLayoutPanel>().First();
            string title = rows.GetControlFromPosition(0, 0)?.Text ?? card.Name;
            if (card.Name == "aiCooldownPreviewCard") title = "技能冷却预览";
            if (card.Name == "aiPerformanceCard") title = "战绩评估与发送";
            menu.Items.Add(new AntdUI.MenuItem { Name = card.Name, Text = title, Tag = card });
        }
        AntdUI.YScrollBar? observedScroll = null;
        Control? clickedSection = null;
        bool jumping = false;
        void ScrollChanged(object sender, AntdUI.IntEventArgs e)
        {
            if (!jumping) clickedSection = null;
            Synchronize();
        }
        void Synchronize()
        {
            if (!page.IsHandleCreated || sections.Length == 0 || page.ClientSize.Height <= 0) return;
            var scroll = page.YScroll;
            if (scroll != null && !ReferenceEquals(scroll, observedScroll))
            {
                if (observedScroll != null) observedScroll.ValueChanged -= ScrollChanged;
                observedScroll = scroll; scroll.ValueChanged += ScrollChanged;
            }
            Control active = sections[0];
            int top = page.PointToScreen(new Point(0, page.Padding.Top + 8 * page.DeviceDpi / 96)).Y;
            foreach (var section in sections)
                if (section.PointToScreen(Point.Empty).Y <= top) active = section;
            // The final card may be shorter than the viewport and cannot reach the top.
            if (scroll is { Value: > 0, IsAtBottom: true })
                active = sections[^1];
            if (clickedSection != null) active = clickedSection;
            bool changed = false;
            foreach (AntdUI.MenuItem item in menu.Items)
            {
                bool selected = ReferenceEquals(item.Tag, active);
                if (item.Select == selected) continue;
                item.Select = selected; changed = true;
            }
            if (changed) menu.Invalidate();
        }
        menu.ItemClick += (_, e) =>
        {
            if (e.Item.Tag is not Control card || card.IsDisposed) return;
            HideSettingTips();
            clickedSection = card; jumping = true;
            try
            {
                if (page.YScroll is { } scroll) scroll.Value = scroll.GetValue(Math.Max(0, card.Top));
                Synchronize();
            }
            finally { jumping = false; }
        };
        page.HandleCreated += (_, _) => Synchronize();
        page.Layout += (_, _) => Synchronize();
        page.VisibleChanged += (_, _) => Synchronize();
        void ResizeDirectory()
        {
            directory.Width = (host.ClientSize.Width < 1100 * host.DeviceDpi / 96 ? 180 : 210) * host.DeviceDpi / 96;
            Synchronize();
        }
        host.SizeChanged += (_, _) => ResizeDirectory();
        host.DpiChangedAfterParent += (_, _) => ResizeDirectory();
        ResizeDirectory();
        host.ResumeLayout(true);
        return host;
    }
}
