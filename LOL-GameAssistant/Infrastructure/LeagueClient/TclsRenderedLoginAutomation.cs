using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using LOL_GameAssistant.Infrastructure.GameData;
using Sdcb.SimdPaddleOCR;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

internal sealed record TclsLoginText(string Text, Rectangle Bounds, float Confidence = 1);
internal sealed record TclsRenderedLoginPlan(Point? QqLogin, Point? Account, Point? Password, Point? Submit, Rectangle? PasswordBounds = null);

/// <summary>TPF renders its password field and buttons without UIA patterns. Recognize the
/// launcher labels locally, then send input only while that verified launcher owns the foreground.</summary>
internal sealed class TclsRenderedLoginAutomation : IDisposable
{
    private PaddleOcrAll? _ocr;
    private DateTime _nextScan;
    private readonly HashSet<IntPtr> _activationAttempted = [];

    internal TclsLoginStep? Step(IntPtr[] windows, TclsLoginSession session,
        string username, string password, CancellationToken token)
    {
        if (DateTime.UtcNow < _nextScan) return null;
        _nextScan = DateTime.UtcNow.AddSeconds(1);
        foreach (var window in windows)
        {
            var title = new System.Text.StringBuilder(256);
            GetWindowText(window, title, title.Capacity);
            if (!title.ToString().Contains("英雄联盟", StringComparison.Ordinal)) continue;
            // One activation per verified launcher window. Never keep stealing focus from the user.
            if (!session.Submitted && _activationAttempted.Add(window))
            {
                if (IsIconic(window)) ShowWindowAsync(window, 9);
                SetForegroundWindow(window);
                return new("已请求将英雄联盟登录程序置前，等待登录面板就绪。");
            }
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
            if (session.CredentialsAttempted)
                return new("密码输入尚未确认，已停止自动提交。请检查登录器密码框。", true);
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
                // Rendered fields apply mouse focus asynchronously, unlike native ValuePattern.
                if (token.WaitHandle.WaitOne(150)) token.ThrowIfCancellationRequested();
                Guard(Screen(point));
                Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
                // This path is only for the account field; passwords use the focused editor below.
                foreach (char character in value) { Guard(Screen(point));Keyboard.Type(character); }
            }
            if (plan.QqLogin is { } qq)
            {
                if (session.EnteredQqLogin) return null;
                session.EnteredQqLogin = true;Click(qq);
                return new("已选择 QQ 登录，等待账号密码表单。");
            }
            // Attempt and submission are different states. Empty/rejected input must never advance to regions.
            session.CredentialsAttempted = true;
            Fill(plan.Account!.Value, username);
            var passwordPoint = plan.Password!.Value;
            Click(passwordPoint);
            for (int attempt = 0; attempt < 10; attempt++)
            {
                if (token.WaitHandle.WaitOne(100)) token.ThrowIfCancellationRequested();
                Guard(Screen(passwordPoint));
                if (TclsPasswordKeyboard.HasFocusedEditor(window, Screen(passwordPoint))) break;
            }
            if (!TclsPasswordKeyboard.HasFocusedEditor(window, Screen(passwordPoint)))
            {
                return new("密码框未取得输入焦点，已停止提交，请点击密码框后重新启动自动登录。", true);
            }
            void PasswordGuard()
            {
                Guard(Screen(passwordPoint));
                if (!TclsPasswordKeyboard.HasFocusedEditor(window, Screen(passwordPoint)))
                    throw new OperationCanceledException("密码框已失去输入焦点，停止输入。");
            }
            if (token.WaitHandle.WaitOne(300)) token.ThrowIfCancellationRequested();
            if (!TclsPasswordKeyboard.Replace(window, password, PasswordGuard, token))
                return new("键盘暂不能可靠输入密码，已停止提交。请松开修饰键并检查输入法后重试，或手动输入密码。", true);
            var field = plan.PasswordBounds!.Value;
            bool confirmed = false;
            for (int attempt = 0; attempt < 5 && !confirmed; attempt++)
            {
                if (token.WaitHandle.WaitOne(150)) token.ThrowIfCancellationRequested();
                Guard(Screen(passwordPoint));
                using var feedback = new Bitmap(field.Width, field.Height, PixelFormat.Format24bppRgb);
                using (var graphics = Graphics.FromImage(feedback))
                    graphics.CopyFromScreen(Screen(field.Location), Point.Empty, field.Size);
                confirmed = TclsPasswordFeedback.IsComplete(feedback, password.Length);
            }
            if (!confirmed)
                return new("未确认密码框完整接收输入，已停止提交。请检查密码框并手动完成登录；不会重复尝试密码。", true);
            session.Submitted = true;
            Click(plan.Submit!.Value);
            return new("已确认密码掩码完整并提交一次，等待登录结果或大区选择。");
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
            int inset = Math.Max(3, gap / 12), halfHeight = Math.Max(6, gap / 6);
            int left = account.Bounds.Left + inset;
            var passwordBounds = Rectangle.FromLTRB(left, passwordPoint.Y - halfHeight,
                passwordPoint.X * 2 - left, passwordPoint.Y + halfHeight);
            if (passwordBounds.Width <= 0 || !new Rectangle(Point.Empty, panel).Contains(passwordBounds)) return null;
            return new(null, accountPoint, passwordPoint, Center(submit.Bounds), passwordBounds);
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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
}
