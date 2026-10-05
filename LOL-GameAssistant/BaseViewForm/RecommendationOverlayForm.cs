using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>
/// 不抢焦点的游戏内建议浮窗。背景完全透明、只绘制文字，并用深色描边保证在亮画面上也能看清；
/// 不读取或覆盖游戏画面。
/// 逐像素透明需要分层窗口（WS_EX_LAYERED + UpdateLayeredWindow），而分层窗口不承载子控件，
/// 所以标题、正文与依据都在这里手工排版绘制。
/// </summary>
internal sealed class RecommendationOverlayForm : AntdUI.Window
{
    private const int WsExNoActivate = 0x08000000;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExLayered = 0x00080000;

    private const int UlwAlpha = 0x00000002;
    private const byte AcSrcOver = 0x00;
    private const byte AcSrcAlpha = 0x01;

    /// <summary>正文最大宽度，超出后换行；窗口高度按内容自适应。</summary>
    private const int MaxTextWidth = 470;

    private const int TextPadding = 8;

    private const string HeaderText = "LOL 助手 · 当前时间线建议";

    private static readonly Color HeaderColor = Color.FromArgb(160, 212, 252);
    private static readonly Color TitleColor = Color.FromArgb(255, 255, 255);
    private static readonly Color BodyColor = Color.FromArgb(238, 242, 247);
    private static readonly Color EvidenceColor = Color.FromArgb(186, 196, 208);
    private static readonly Color OutlineColor = Color.FromArgb(205, 0, 0, 0);

    private readonly System.Windows.Forms.Timer _hideTimer = new();
    private readonly Font _headerFont = new("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
    private readonly Font _titleFont = new("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
    private readonly Font _bodyFont = new("Microsoft YaHei UI", 10F);
    private readonly Font _evidenceFont = new("Microsoft YaHei UI", 8.5F);

    private readonly StringFormat _textFormat = new(StringFormatFlags.LineLimit)
    {
        Trimming = StringTrimming.EllipsisCharacter
    };

    private string _title = "";
    private string _body = "";
    private string _evidence = "";

    /// <summary>初始化 RecommendationOverlayForm 的实例状态。</summary>
    public RecommendationOverlayForm()
    {
        AntdWindowChrome.Configure(this);
        EnableHitTest = false;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(MaxTextWidth + TextPadding * 2, 96);

        _hideTimer.Tick += (_, _) => Hide();
        Disposed += (_, _) =>
        {
            _hideTimer.Dispose();
            _headerFont.Dispose();
            _titleFont.Dispose();
            _bodyFont.Dispose();
            _evidenceFont.Dispose();
            _textFormat.Dispose();
        };
    }

    /// <summary>显示浮窗时不抢占游戏或客户端的输入焦点。</summary>
    protected override bool ShowWithoutActivation => true;

    /// <summary>保留无激活或分层透明等浮窗需要的原生窗口标志。</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            // 不激活、不进入 Alt+Tab，鼠标可穿透到游戏窗口；透明由分层位图逐像素决定。
            parameters.ExStyle |= WsExNoActivate | WsExTransparent | WsExToolWindow | WsExLayered;
            return parameters;
        }
    }

    /// <summary>内容全部来自分层位图，屏蔽默认背景与重绘，避免闪一下不透明底色。</summary>
    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    /// <summary>绘制当前控件的自定义外观。</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
    }

    /// <summary>更新游戏内推荐文字并按配置时长显示浮窗。</summary>
    public void ShowRecommendation(CoachRecommendation recommendation, CloudAiSettings settings)
    {
        if (IsDisposed || string.IsNullOrWhiteSpace(recommendation.Body)) return;

        _title = string.IsNullOrWhiteSpace(recommendation.Title) ? "" : Clip(recommendation.Title, 40);
        _body = Clip(recommendation.Body, 240);
        _evidence = string.IsNullOrWhiteSpace(recommendation.Evidence) ? "" : "依据：" + Clip(recommendation.Evidence, 120);

        using (Graphics screen = CreateGraphics())
        {
            ResizeToContent(screen);
        }
        Location = GetLocation(settings);
        RenderLayered();

        _hideTimer.Stop();
        _hideTimer.Interval = Math.Clamp(settings.RecommendationOverlayDurationSeconds, 3, 30) * 1000;
        Show();
        _hideTimer.Start();
    }

    /// <summary>超长文本截断，避免浮窗高到遮住半个屏幕。</summary>
    private static string Clip(string text, int maxLength)
    {
        string trimmed = text.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "…";
    }

    /// <summary>按内容量出窗口大小；宽度固定，高度随文字行数变化。</summary>
    private void ResizeToContent(Graphics screen)
    {
        float contentWidth = MaxTextWidth;
        float height = TextPadding;
        height += MeasureHeight(screen, HeaderText, _headerFont, contentWidth);
        height += MeasureHeight(screen, _title, _titleFont, contentWidth);
        height += MeasureHeight(screen, _body, _bodyFont, contentWidth);
        height += MeasureHeight(screen, _evidence, _evidenceFont, contentWidth);
        height += TextPadding;

        Size = new Size(
            (int)Math.Ceiling(contentWidth) + TextPadding * 2,
            (int)Math.Ceiling(height));
    }

    /// <summary>按指定字体和宽度测量文本所需高度。</summary>
    private float MeasureHeight(Graphics graphics, string text, Font font, float width) =>
        string.IsNullOrEmpty(text) ? 0 : graphics.MeasureString(text, font, new SizeF(width, 10000), _textFormat).Height;

    /// <summary>将浮窗文字绘制为带透明通道的分层位图。</summary>
    private void RenderLayered()
    {
        using Graphics screen = CreateGraphics();
        // 位图分辨率与屏幕一致，否则在缩放显示器上量出来的字号和画出来的不一致。
        // 直接用 PArgb：分层窗口要求预乘 alpha，GDI+ 画到这个格式上就是预乘结果。
        using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
        bitmap.SetResolution(screen.DpiX, screen.DpiY);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            // 灰度抗锯齿：ClearType 在透明底上会留下彩色边缘。
            graphics.TextRenderingHint = TextRenderingHint.AntiAlias;

            float width = Width - TextPadding * 2;
            // 描边宽度跟随 DPI：缩放显示器上字号变大，1 像素的描边会显得过细。
            float outlineOffset = Math.Max(1F, screen.DpiX / 96F);
            float y = TextPadding;
            y = DrawBlock(graphics, HeaderText, _headerFont, HeaderColor, width, y, outlineOffset);
            y = DrawBlock(graphics, _title, _titleFont, TitleColor, width, y, outlineOffset);
            y = DrawBlock(graphics, _body, _bodyFont, BodyColor, width, y, outlineOffset);
            DrawBlock(graphics, _evidence, _evidenceFont, EvidenceColor, width, y, outlineOffset);
        }

        ApplyLayeredBitmap(bitmap);
    }

    /// <summary>先沿八个方向描一圈深色、再填色：背景透明后，亮画面上仍能看清文字。</summary>
    private float DrawBlock(Graphics graphics, string text, Font font, Color color, float width, float y, float outlineOffset)
    {
        if (string.IsNullOrEmpty(text)) return y;

        var area = new RectangleF(TextPadding, y, width, 10000);
        float height = graphics.MeasureString(text, font, new SizeF(width, 10000), _textFormat).Height;

        using var outline = new SolidBrush(OutlineColor);
        for (int stepX = -1; stepX <= 1; stepX++)
        {
            for (int stepY = -1; stepY <= 1; stepY++)
            {
                if (stepX == 0 && stepY == 0) continue;
                float dx = stepX * outlineOffset;
                float dy = stepY * outlineOffset;
                graphics.DrawString(
                    text,
                    font,
                    outline,
                    new RectangleF(area.X + dx, area.Y + dy, area.Width, area.Height),
                    _textFormat);
            }
        }

        using var fill = new SolidBrush(color);
        graphics.DrawString(text, font, fill, area, _textFormat);
        return y + height;
    }

    /// <summary>将透明位图提交给原生分层窗口。</summary>
    private void ApplyLayeredBitmap(Bitmap bitmap)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memoryDc = CreateCompatibleDC(screenDc);
        IntPtr bitmapHandle = IntPtr.Zero;
        IntPtr previousBitmap = IntPtr.Zero;
        try
        {
            bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
            previousBitmap = SelectObject(memoryDc, bitmapHandle);

            var size = new Size(bitmap.Width, bitmap.Height);
            var sourcePoint = new Point(0, 0);
            var targetPoint = new Point(Left, Top);
            var blend = new BlendFunction
            {
                BlendOp = AcSrcOver,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AcSrcAlpha
            };

            UpdateLayeredWindow(
                Handle,
                screenDc,
                ref targetPoint,
                ref size,
                memoryDc,
                ref sourcePoint,
                0,
                ref blend,
                UlwAlpha);
        }
        finally
        {
            if (bitmapHandle != IntPtr.Zero)
            {
                SelectObject(memoryDc, previousBitmap);
                DeleteObject(bitmapHandle);
            }
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>根据屏幕区域和浮窗设置计算显示位置。</summary>
    private Point GetLocation(CloudAiSettings settings)
    {
        Rectangle area = Screen.FromHandle(GetLeagueWindowHandle() ?? Program.GameMain.Handle).WorkingArea;
        int x = area.Left + settings.RecommendationOverlayOffsetX;
        int y = area.Bottom - Height - settings.RecommendationOverlayOffsetY;
        switch (settings.RecommendationOverlayPosition)
        {
            case "TopLeft":
                y = area.Top + settings.RecommendationOverlayOffsetY;
                break;

            case "TopRight":
                x = area.Right - Width - settings.RecommendationOverlayOffsetX;
                y = area.Top + settings.RecommendationOverlayOffsetY;
                break;

            case "BottomRight":
                x = area.Right - Width - settings.RecommendationOverlayOffsetX;
                break;

            case "Center":
                x = area.Left + (area.Width - Width) / 2 + settings.RecommendationOverlayOffsetX;
                y = area.Top + (area.Height - Height) / 2 + settings.RecommendationOverlayOffsetY;
                break;
        }
        int maxX = Math.Max(area.Left, area.Right - Width);
        int maxY = Math.Max(area.Top, area.Bottom - Height);
        return new Point(Math.Clamp(x, area.Left, maxX), Math.Clamp(y, area.Top, maxY));
    }

    /// <summary>优先跟随游戏窗口所在显示器；未找到时才回退到助手主窗口。</summary>
    private static IntPtr? GetLeagueWindowHandle()
    {
        IntPtr foreground = GetForegroundWindow();
        if (IsLeagueWindow(foreground)) return foreground;

        foreach (string processName in new[] { "League of Legends", "LeagueClientUx", "LeagueClient" })
        {
            foreach (Process process in Process.GetProcessesByName(processName))
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero) return process.MainWindowHandle;
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        return null;
    }

    /// <summary>判断指定窗口是否属于游戏进程。</summary>
    private static bool IsLeagueWindow(IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        GetWindowThreadProcessId(window, out uint processId);
        if (processId == 0) return false;
        try
        {
            using Process process = Process.GetProcessById((int)processId);
            return process.ProcessName.Equals("League of Legends", StringComparison.OrdinalIgnoreCase) ||
                   process.ProcessName.Equals("LeagueClientUx", StringComparison.OrdinalIgnoreCase) ||
                   process.ProcessName.Equals("LeagueClient", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>原生分层窗口更新使用的透明混合参数。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    /// <summary>读取当前前台窗口的句柄。</summary>
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    /// <summary>读取窗口所属线程和进程标识。</summary>
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    /// <summary>将带透明通道的位图更新到分层窗口。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr window,
        IntPtr destinationDc,
        ref Point destinationPoint,
        ref Size size,
        IntPtr sourceDc,
        ref Point sourcePoint,
        int colorKey,
        ref BlendFunction blend,
        int flags);

    /// <summary>取得指定窗口或屏幕的设备上下文。</summary>
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    /// <summary>归还从窗口或屏幕取得的设备上下文。</summary>
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    /// <summary>创建用于离屏绘制的兼容设备上下文。</summary>
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    /// <summary>释放由绘图流程创建的设备上下文。</summary>
    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);

    /// <summary>将绘图对象选入设备上下文，并返回此前的对象。</summary>
    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);

    /// <summary>释放原生 GDI 绘图对象。</summary>
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr value);
}
