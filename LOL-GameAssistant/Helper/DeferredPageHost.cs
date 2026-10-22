namespace LOL_GameAssistant.Helper;

/// <summary>先绘制当前主题的等待内容，再在首次打开页面时创建控件。</summary>
internal sealed class DeferredPageHost : UserControl, IThemeAware
{
    private readonly Func<Control> _factory;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 35 };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
        Text = "正在加载页面…", AutoEllipsis = true
    };
    private readonly Button _retry = new() { Dock = DockStyle.Bottom, Height = 40, Text = "重新加载", Visible = false };
    private bool _loaded;
    private bool _loading;
    private readonly List<AntdUI.Tabs> _tabOwners = [];

    internal DeferredPageHost(Func<Control> factory)
    {
        _factory = factory;
        Dock = DockStyle.Fill;
        DoubleBuffered = true;
        Controls.Add(_status);
        Controls.Add(_retry);
        _timer.Tick += (_, _) => LoadPage();
        _retry.Click += (_, _) =>
        {
            _retry.Visible = false;
            _status.Text = UiLanguage.T("正在加载页面…");
            ScheduleLoad();
        };
        ApplyTheme(UiTheme.Palette);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        BindTabSelection();
        ScheduleLoad();
    }

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        BindTabSelection();
        ScheduleLoad();
    }

    private void BindTabSelection()
    {
        foreach (var tabs in _tabOwners) tabs.SelectedIndexChanged -= TabSelectionChanged;
        _tabOwners.Clear();
        for (Control? ancestor = Parent; ancestor != null; ancestor = ancestor.Parent)
            if (ancestor is AntdUI.Tabs tabs)
            {
                _tabOwners.Add(tabs);
                tabs.SelectedIndexChanged += TabSelectionChanged;
            }
    }

    private void TabSelectionChanged(object? sender, EventArgs args)
    {
        _timer.Stop();
        ScheduleLoad();
    }

    private bool IsSelectedPage()
    {
        // AntdUI moves inactive pages outside the viewport without hiding them;
        // Control.Visible alone does not mean the user has opened this page.
        for (Control? ancestor = Parent; ancestor != null; ancestor = ancestor.Parent)
            if (ancestor is AntdUI.TabPage page && page.Parent is AntdUI.Tabs tabs &&
                (tabs.SelectedIndex < 0 || tabs.SelectedIndex >= tabs.Pages.Count ||
                 !ReferenceEquals(tabs.Pages[tabs.SelectedIndex], page))) return false;
        return true;
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible) _timer.Stop();
        else ScheduleLoad();
    }

    private void ScheduleLoad()
    {
        if (!IsDisposed && !Disposing && IsHandleCreated && Visible && IsSelectedPage() && !_loaded && !_loading)
            _timer.Start();
    }

    private void LoadPage()
    {
        _timer.Stop();
        if (IsDisposed || Disposing || !Visible || !IsSelectedPage() || _loaded || _loading) return;
        _loading = true;
        Control? page = null;
        try
        {
            // Flush the placeholder before synchronous WinForms control construction.
            Update();
            using var performance = new UiPerformanceSample("首次打开页面");
            page = _factory();
            if (IsDisposed || Disposing) { page.Dispose(); return; }
            page.Visible = false;
            page.Dock = DockStyle.Fill;
            UiTheme.Apply(page);
            SuspendLayout();
            try
            {
                Controls.Add(page);
                page.BringToFront();
                page.Visible = true;
                _status.Visible = false;
                _retry.Visible = false;
                _loaded = true;
            }
            finally { ResumeLayout(true); }
            UiLanguage.Refresh();
        }
        catch (Exception ex)
        {
            _loaded = false;
            if (page != null && !page.IsDisposed) page.Dispose();
            if (IsDisposed || Disposing) return;
            _status.Visible = true;
            _status.Text = $"{UiLanguage.T("页面加载失败")}：{ex.Message}";
            _retry.Visible = true;
            _retry.BringToFront();
            RuntimeDiagnostics.WriteException(ex);
        }
        finally { _loading = false; }
    }

    public void ApplyTheme(ThemePalette palette)
    {
        BackColor = _status.BackColor = _retry.BackColor = palette.Surface;
        ForeColor = _status.ForeColor = _retry.ForeColor = palette.TextSecondary;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            foreach (var tabs in _tabOwners) tabs.SelectedIndexChanged -= TabSelectionChanged;
            _tabOwners.Clear();
        }
        base.Dispose(disposing);
    }
}
