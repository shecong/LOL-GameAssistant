using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>Brief, click-through confirmation that never takes focus from the game.</summary>
internal sealed class SkinChangeOverlayForm : Form
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    private readonly Label _label;
    private long _hideAt;

    public SkinChangeOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        // Color-key transparency removes the entire panel while keeping the text opaque.
        BackColor = Color.Fuchsia;
        TransparencyKey = BackColor;
        ClientSize = new Size(520, 64);
        _label = new Label
        {
            Dock = DockStyle.Fill, ForeColor = Color.White, BackColor = BackColor,
            Font = new Font(UiMetrics.FontFamily, 12, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter, AutoEllipsis = true,
            Padding = new Padding(16, 8, 16, 8)
        };
        Controls.Add(_label);
        _timer.Tick += (_, _) =>
        {
            if (Environment.TickCount64 >= _hideAt || !WindowHoldController.TryGetForegroundGameBounds(out var bounds))
            {
                Hide();
                _timer.Stop();
                return;
            }
            PositionOverGame(bounds);
        };
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000000 | 0x00000080 | 0x00000020;
            return parameters;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0084) { m.Result = new IntPtr(-1); return; }
        if (m.Msg == 0x0021) { m.Result = new IntPtr(3); return; }
        base.WndProc(ref m);
    }

    internal void ShowSkin(string name)
    {
        if (!WindowHoldController.TryGetForegroundGameBounds(out var bounds)) return;
        _label.Text = $"{UiLanguage.T("已切换皮肤")}：{name}";
        PositionOverGame(bounds);
        _hideAt = Environment.TickCount64 + 3000;
        Show();
        _timer.Start();
    }

    private void PositionOverGame(Rectangle bounds) =>
        Location = new Point(bounds.Left + (bounds.Width - Width) / 2, bounds.Top + Math.Max(24, bounds.Height / 8));

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Dispose(); _label.Font.Dispose(); }
        base.Dispose(disposing);
    }
}
