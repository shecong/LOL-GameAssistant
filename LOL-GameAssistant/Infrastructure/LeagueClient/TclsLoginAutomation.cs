using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

internal sealed class TclsLoginSession
{
    internal bool EnteredQqLogin, Switched, CredentialsAttempted, Submitted, ExpandedRegion, RegionSelected, Launched;
}

internal sealed record TclsLoginStep(string Message, bool Pause = false);

/// <summary>Native accessible login forms; TPF's rendered form uses a separate visual adapter.</summary>
internal static class TclsLoginAutomation
{
    private static readonly string[] Verification = ["请输入验证码", "安全验证", "短信验证码", "滑动验证", "账号或密码错误", "账号或密码不正确", "密码错误", "登录失败", "账号被冻结"];
    internal static bool HasVerification(string text) => Verification.Any(value => text.Contains(value, StringComparison.Ordinal));
    internal static TclsLoginStep Step(AutomationElement[] roots, TclsLoginSession session,
        string username, string password, string region, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var elements = roots.SelectMany(root => root.FindAllDescendants()).Where(element =>
            element.Properties.IsEnabled.ValueOrDefault && !element.Properties.IsOffscreen.ValueOrDefault).ToArray();
        // Inspect labels, never inspect an Edit's name or value for diagnostics/verification.
        var labels = elements.Where(element => element.ControlType != ControlType.Edit)
            .Select(element => element.Name ?? "").ToArray();
        if (labels.Any(HasVerification))
            return new("检测到验证码、安全验证或登录错误，自动登录已暂停，请在 TCLS 窗口处理后重新启动登录流程。", true);
        if (session.Launched) return new("已选择大区并请求进入游戏，等待客户端连接。");
        if (session.CredentialsAttempted && !session.Submitted)
            return new("密码输入尚未确认，已停止自动提交。请检查登录器密码框。", true);

        if (!session.Submitted)
        {
            var edits = elements.Where(element => element.ControlType == ControlType.Edit).ToArray();
            var passwords = edits.Where(element => element.Properties.IsPassword.ValueOrDefault).ToArray();
            var accounts = edits.Where(element => !element.Properties.IsPassword.ValueOrDefault).ToArray();
            var login = UniqueAction(elements, ["登录", "登 录", "登录游戏", "账号登录"]);
            if (passwords.Length == 1 && accounts.Length == 1 && login != null &&
                accounts[0].Patterns.Value.TryGetPattern(out var accountValue) &&
                passwords[0].Patterns.Value.TryGetPattern(out var passwordValue) &&
                !accountValue.IsReadOnly.Value && !passwordValue.IsReadOnly.Value)
            {
                token.ThrowIfCancellationRequested();
                session.Submitted = true; // Any later failure must not cause automatic resubmission.
                accountValue.SetValue(username);
                token.ThrowIfCancellationRequested();
                passwordValue.SetValue(password);
                token.ThrowIfCancellationRequested();
                login.Patterns.Invoke.Pattern.Invoke();
                return new("已填写账号密码并提交一次，等待 TCLS 返回登录结果。");
            }
            var switchLogin = UniqueAction(elements, ["账号密码登录", "帐号密码登录", "密码登录", "使用账号密码登录"]);
            if (switchLogin != null && !session.Switched)
            {
                session.Switched = true;token.ThrowIfCancellationRequested();switchLogin.Patterns.Invoke.Pattern.Invoke();
                return new("已切换到账号密码登录，等待登录控件就绪。");
            }
            return new("等待 TCLS 账号密码控件；暂未识别到唯一、可填写的登录表单。");
        }

        if (!session.RegionSelected)
        {
            var combos = elements.Where(element => element.ControlType == ControlType.ComboBox).ToArray();
            if (combos.Length == 1)
            {
                var selected = combos[0].AsComboBox().SelectedItem;
                if (selected != null && RegionMatches(selected.Name, region)) session.RegionSelected = true;
            }
            if (!session.RegionSelected)
            {
                var choices = elements.Where(element => RegionMatches(element.Name, region) &&
                    element.ControlType is ControlType.ListItem or ControlType.TreeItem or ControlType.RadioButton or ControlType.Button).ToArray();
                if (choices.Length == 1)
                {
                    token.ThrowIfCancellationRequested();
                    if (choices[0].Patterns.SelectionItem.TryGetPattern(out var selection)) selection.Select();
                    else if (choices[0].Patterns.Invoke.TryGetPattern(out var invoke)) invoke.Invoke();
                    else return new("已找到大区，但控件不支持可靠选择，请在 TCLS 中手动选择。", true);
                    session.RegionSelected = true;
                    return new("已选择默认大区，等待进入游戏按钮。");
                }
                if (combos.Length == 1 && !session.ExpandedRegion && combos[0].Patterns.ExpandCollapse.TryGetPattern(out var expand))
                {
                    session.ExpandedRegion = true;token.ThrowIfCancellationRequested();expand.Expand();
                    return new("已展开大区列表，等待目标大区出现。");
                }
                return new("等待默认大区选项；未确认目标大区前不会启动游戏。");
            }
        }
        var start = UniqueAction(elements, ["进入游戏", "开始游戏", "启动游戏", "进入英雄联盟"]);
        if (start == null) return new("已确认默认大区，等待可识别的进入游戏按钮。");
        token.ThrowIfCancellationRequested();session.Launched = true;start.Patterns.Invoke.Pattern.Invoke();
        return new("已选择大区并请求进入游戏，等待客户端连接。");
    }

    private static AutomationElement? UniqueAction(AutomationElement[] elements, string[] names)
    {
        var matches = elements.Where(element => element.ControlType is ControlType.Button or ControlType.Hyperlink &&
            names.Contains(element.Name?.Trim(), StringComparer.Ordinal) && element.Patterns.Invoke.IsSupported).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
    internal static bool RegionMatches(string? name, string region) =>
        string.Equals(name?.Trim(), region.Trim(), StringComparison.Ordinal);
}
