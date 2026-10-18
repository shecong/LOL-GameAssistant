namespace LOL_GameAssistant.Helper;

/// <summary>订阅已检测的新版本，支持延迟创建的页面和关闭弹窗后重新打开。</summary>
internal static class UpdateBadge
{
    internal static AntdUI.Tag Create(string name, Action<string>? openUrl = null)
    {
        var badge = new AntdUI.Tag
        {
            Name = name, Text = "有新版本更新", Size = new Size(116, 24),
            Type = AntdUI.TTypeMini.Primary, Cursor = Cursors.Hand, Visible = false,
            Margin = new Padding(4, 6, 4, 0)
        };
        void Refresh(object? sender, EventArgs e)
        {
            if (badge.IsDisposed) return;
            if (badge.InvokeRequired) { badge.BeginInvoke(() => Refresh(sender, e)); return; }
            badge.Text = UiLanguage.IsEnglish ? "Update" : "有新版本更新";
            badge.Visible = AppUpdateChecker.AvailableRelease != null;
        }
        badge.Click += (_, _) =>
        {
            if (badge.FindForm() is { } owner) AppUpdateChecker.ShowAvailable(owner, openUrl);
        };
        AppUpdateChecker.AvailabilityChanged += Refresh;
        UiLanguage.Changed += Refresh;
        badge.Disposed += (_, _) =>
        {
            AppUpdateChecker.AvailabilityChanged -= Refresh;
            UiLanguage.Changed -= Refresh;
        };
        Refresh(null, EventArgs.Empty);
        return badge;
    }

    internal static void AddToHeader(AntdUI.PageHeader header)
    {
        var badge = Create("HeaderUpdateBadge");
        header.Controls.Add(badge);
        header.Layout += (_, _) =>
        {
            int Scale(int value) => value * header.DeviceDpi / 96;
            int titleWidth = TextRenderer.MeasureText(header.Text, header.Font).Width;
            badge.Bounds = new Rectangle(header.Height + titleWidth + Scale(24),
                Math.Max(0, (header.Height - Scale(24)) / 2), Scale(116), Scale(24));
        };
        badge.BringToFront();
    }
}
