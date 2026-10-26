using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using LOL_GameAssistant.Infrastructure.GameData;
using Sdcb.SimdPaddleOCR;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

internal sealed record TclsLoginText(string Text, Rectangle Bounds, float Confidence = 1);
internal sealed record TclsRenderedLoginPlan(Point? QqLogin, Point? Account, Point? Password, Point? Submit);

/// <summary>TPF renders its password field and buttons without UIA patterns. Recognize the
/// launcher labels locally, then send input only while that verified launcher owns the foreground.</summary>
internal sealed class TclsRenderedLoginAutomation : IDisposable
{
    private PaddleOcrAll? _ocr;
    private DateTime _nextScan;

    internal TclsLoginStep? Step(AutomationElement[] roots, TclsLoginSession session,
        string username, string password, CancellationToken token)
    {
        if (DateTime.UtcNow < _nextScan) return null;
        _nextScan = DateTime.UtcNow.AddSeconds(1);
        foreach (var root in roots)
        {
            if (!root.Name.Contains("英雄联盟", StringComparison.Ordinal)) continue;
            var window = new IntPtr(root.Properties.NativeWindowHandle.ValueOrDefault);
            if (window == IntPtr.Zero || !GetClientRect(window, out var rect)) continue;
            Point origin = Point.Empty;
            if (!ClientToScreen(window, ref origin) || rect.Right < 800 || rect.Bottom < 450) continue;
            // Capture only the login panel. Never OCR the desktop or save credentials/screenshots.
            var panel = new Rectangle(origin.X + rect.Right * 3 / 4, origin.Y,
                rect.Right - rect.Right * 3 / 4, rect.Bottom);
            if (!OwnsForeground(window, panel))
                return new("请将英雄联盟登录程序置于前台并保持登录面板可见，等待识别 QQ 登录表单。");
            using var image = new Bitmap(panel.Width, panel.Height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(image))
                graphics.CopyFromScreen(panel.Location, Point.Empty, panel.Size);
            token.ThrowIfCancellationRequested();
            var labels = ReadLabels(image);
            if (labels.Any(label => TclsLoginAutomation.HasVerification(label.Text)))
                return new("检测到验证码、安全验证或登录错误，已停止自动操作，请在登录器中手动处理。", true);
            if (session.Submitted) return null;
            var plan = Plan(labels, image.Size);
            if (plan == null) return null;
            Point Screen(Point point) => new(panel.Left + point.X, panel.Top + point.Y);
            void Guard(Point point)
            {
                token.ThrowIfCancellationRequested();
                if (!OwnsForeground(window, panel) || GetAncestor(WindowFromPoint(point), 2) != window)
                    throw new OperationCanceledException("登录窗口已失去前台或被遮挡，停止输入。");
            }
            void Click(Point point)
            {
                point = Screen(point);Guard(point);Mouse.Click(point, MouseButton.Left);
            }
            void Fill(Point point, string value)
            {
                Click(point);
                Guard(Screen(point));
                Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
                // Individual Unicode keystrokes allow cancellation/focus checks throughout a password.
                foreach (char character in value) { Guard(Screen(point));Keyboard.Type(character); }
            }
            if (plan.QqLogin is { } qq)
            {
                if (session.EnteredQqLogin) return null;
                session.EnteredQqLogin = true;Click(qq);
                return new("已选择 QQ 登录，等待账号密码表单。");
            }
            // Mark before typing: focus loss or a partial failure must never submit credentials twice.
            session.Submitted = true;
            Fill(plan.Account!.Value, username);Fill(plan.Password!.Value, password);
            Click(plan.Submit!.Value);
            return new("已在 QQ 登录表单填写账号密码并请求进入游戏，等待登录结果或大区选择。");
        }
        return null;
    }

    internal TclsLoginText[] ReadLabels(Bitmap image)
    {
        _ocr ??= AugmentOcrEngine.Create();
        var result = AugmentImageRecognizer.ReadImage(image, _ocr);
        return result.Lines.Select(line =>
        {
            var box = line.Box;
            return new TclsLoginText(line.Text, Rectangle.FromLTRB(
                (int)Math.Min(Math.Min(box.X1, box.X2), Math.Min(box.X3, box.X4)),
                (int)Math.Min(Math.Min(box.Y1, box.Y2), Math.Min(box.Y3, box.Y4)),
                (int)Math.Max(Math.Max(box.X1, box.X2), Math.Max(box.X3, box.X4)),
                (int)Math.Max(Math.Max(box.Y1, box.Y2), Math.Max(box.Y3, box.Y4))), line.RecognitionScore);
        }).ToArray();
    }

    internal static TclsRenderedLoginPlan? Plan(IReadOnlyList<TclsLoginText> labels, Size panel)
    {
        static string Normalize(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c))).Replace("：", "").Replace(":", "");
        TclsLoginText? Unique(params string[] names)
        {
            var found = labels.Where(label => label.Confidence >= .85f && names.Contains(Normalize(label.Text), StringComparer.OrdinalIgnoreCase)).ToArray();
            return found.Length == 1 ? found[0] : null;
        }
        static Point Center(Rectangle box) => new(box.Left + box.Width / 2, box.Top + box.Height / 2);
        var account = Unique("QQ账号", "QQ帐号");var password = Unique("QQ密码");
        var submit = Unique("进入游戏");
        if (account != null && password != null && submit != null)
        {
            int gap = password.Bounds.Top - account.Bounds.Top;
            // The observed TPF form places a field half a label-row below each heading.
            // Require its complete geometry; never fill from a single OCR word or guessed screen offset.
            if (gap < panel.Height * .05 || gap > panel.Height * .15 ||
                Math.Abs(account.Bounds.Left - password.Bounds.Left) > panel.Width * .08 ||
                submit.Bounds.Top <= password.Bounds.Bottom + gap ||
                Math.Abs(Center(submit.Bounds).X - panel.Width / 2) > panel.Width * .25) return null;
            var accountPoint = new Point(Center(submit.Bounds).X, account.Bounds.Top + gap / 2);
            var passwordPoint = new Point(accountPoint.X, password.Bounds.Top + gap / 2);
            if (!new Rectangle(Point.Empty, panel).Contains(accountPoint) || !new Rectangle(Point.Empty, panel).Contains(passwordPoint)) return null;
            return new(null, accountPoint, passwordPoint, Center(submit.Bounds));
        }
        // The initial chooser contains both providers; don't click QQ again on the account form.
        var qq = Unique("QQ登录");var wechat = Unique("微信登录");
        if (qq != null && wechat != null && account == null && password == null && submit == null &&
            wechat.Bounds.Top > qq.Bounds.Bottom && Math.Abs(qq.Bounds.Left - wechat.Bounds.Left) < panel.Width * .25)
            return new(Center(qq.Bounds), null, null, null);
        return null;
    }

    private static bool OwnsForeground(IntPtr window, Rectangle panel) =>
        GetAncestor(GetForegroundWindow(), 2) == window &&
        new[] { new Point(panel.Left + 4, panel.Top + 4), new Point(panel.Right - 4, panel.Top + 4),
            new Point(panel.Left + 4, panel.Bottom - 4), new Point(panel.Right - 4, panel.Bottom - 4) }
        .All(point => GetAncestor(WindowFromPoint(point), 2) == window);

    public void Dispose() => _ocr?.Dispose();
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
}
