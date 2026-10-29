using System.Text.Json;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Application.ClientFeatures;
using FlaUI.UIA3;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using LOL_GameAssistant.Infrastructure.Settings;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class TclsLoginTests
{
    [Fact]
    public void BrokenAccessibleProviderStillUsesRenderedLogin()
    {
        var expected = new TclsLoginStep("已识别图像登录表单");
        var result = TclsAutomaticClientLauncher.ReadLoginStep(
            () => throw new System.Runtime.InteropServices.COMException("provider unavailable"), () => expected);
        Assert.Same(expected, result);
    }

    [Fact]
    public void VerificationAndCancellationNeverFallThroughToRenderedInput()
    {
        var verification = new TclsLoginStep("需要安全验证", true);
        Assert.Same(verification, TclsAutomaticClientLauncher.ReadLoginStep(() => verification,
            () => throw new InvalidOperationException("must not type")));
        Assert.Throws<OperationCanceledException>(() => TclsAutomaticClientLauncher.ReadLoginStep(
            () => throw new OperationCanceledException(), () => throw new InvalidOperationException("must not type")));
    }

    [Fact]
    public void TencentLauncherDiscoveryIncludesHandoffButNotOtherInstallations()
    {
        Assert.Equal([@"E:\Games\LOL\TCLS\client.exe", @"E:\Games\LOL\Launcher\Client.exe"],
            TclsLauncherWindows.Executables(@"E:\Games\LOL\TCLS\client.exe"));
        Assert.Empty(TclsLauncherWindows.Executables(@"E:\Other\client.exe"));
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        Assert.True(TclsLauncherWindows.MatchesPath(process, process.MainModule!.FileName));
        Assert.False(TclsLauncherWindows.MatchesPath(process, @"E:\Other\client.exe"));
    }

    [Fact]
    public void RenderedLauncherRequestsForegroundOnceAndWaitsAfterUserSwitchesAway() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var launcher = new Form { Text = "英雄联盟登录程序", ClientSize = new Size(1000, 600), ShowInTaskbar = false };
        using var other = new Form { Text = "其他窗口", ShowInTaskbar = false };
        launcher.Show();other.Show();System.Windows.Forms.Application.DoEvents();
        using var rendered = new TclsRenderedLoginAutomation();
        var session = new TclsLoginSession();
        var first = rendered.Step([launcher.Handle], session, "unused-account", "unused-password", default);
        Assert.Contains("置前", first!.Message);
        other.Activate();System.Windows.Forms.Application.DoEvents();
        Thread.Sleep(1100);
        var second = rendered.Step([launcher.Handle], session, "unused-account", "unused-password", default);
        Assert.Contains("请将", second!.Message);
        Assert.False(session.Submitted);
    });

    [Fact]
    public void PasswordIsEncryptedAndWhitespaceSurvivesRestart()
    {
        string path = Path.Combine(Path.GetTempPath(), "tcls-test-" + Guid.NewGuid(), "accounts.json");
        try
        {
            var store = new LocalTclsAccountStore(path);
            string password = "  本地测试! password  ";
            var account = store.Save(null, "测试备注", "123456789", "艾欧尼亚", password);
            Assert.DoesNotContain(password, File.ReadAllText(path));
            Assert.DoesNotContain(" password ", File.ReadAllText(path), StringComparison.Ordinal);
            Assert.Equal(password, new LocalTclsAccountStore(path).ReadPassword(account.Id));
            store.Save(account.Id, "改备注", account.Username, "祖安", "");
            Assert.Equal(password, store.ReadPassword(account.Id));
            Assert.Throws<InvalidOperationException>(() => store.Save(account.Id, "", "987654321", "祖安", ""));
            store.Delete(account.Id);Assert.Empty(store.Load());
        }
        finally { if (File.Exists(path)) File.Delete(path);if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!); }
    }

    [Fact]
    public void DamagedOrForeignCipherRequiresReentryWithoutEchoingSecret()
    {
        string path = Path.Combine(Path.GetTempPath(), "tcls-test-" + Guid.NewGuid() + ".json");
        try
        {
            var store = new LocalTclsAccountStore(path);
            var account = store.Save(null, "备注", "123456789", "艾欧尼亚", "not-a-real-password");
            account.Id = Guid.NewGuid().ToString("N"); // DPAPI entropy binds ciphertext to the account.
            File.WriteAllText(path, JsonSerializer.Serialize(new[] { account }));
            string message = Assert.Throws<InvalidOperationException>(() => store.ReadPassword(account.Id)).Message;
            Assert.Contains("重新保存密码", message);Assert.DoesNotContain("not-a-real-password", message);
            File.WriteAllText(path, "invalid json");
            Assert.Throws<InvalidOperationException>(() => store.Load());
            Assert.Equal("invalid json", File.ReadAllText(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void AutoLoginSelectionSurvivesSettingsMapping()
    {
        var domain = LegacySettingsMapper.ToDomain(new SettingConfig { TclsAutoLoginEnabled = true, TclsSelectedAccountId = "profile-id" });
        var saved = LegacySettingsMapper.ToLegacy(domain);
        Assert.True(saved.TclsAutoLoginEnabled);Assert.Equal("profile-id", saved.TclsSelectedAccountId);
        Assert.False(new AssistantSettings().TclsAutoLoginEnabled);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void AccountDialogNeverDisplaysStoredPassword(string theme) => MatchListScrollingTests.OnUiThread(() =>
    {
        string path = Path.Combine(Path.GetTempPath(), "tcls-dialog-" + Guid.NewGuid() + ".json");
        string previousTheme = LOL_GameAssistant.Helper.UiTheme.Mode;
        try
        {
            LOL_GameAssistant.Helper.UiTheme.SetMode(theme);
            var store = new LocalTclsAccountStore(path);
            var account = store.Save(null, "备注", "123456789", "艾欧尼亚", "not-a-real-password");
            using var dialog = new TclsAccountsForm(store, account.Id);
            dialog.Show();System.Windows.Forms.Application.DoEvents();
            var password = Descendants(dialog).OfType<AntdUI.Input>().Single(control => control.Name == "tclsPassword");
            Assert.True(password.UseSystemPasswordChar);Assert.Empty(password.Text);
            Assert.DoesNotContain("not-a-real-password", string.Join(" ", Descendants(dialog).Select(control => control.Text)));
            string? preview = Environment.GetEnvironmentVariable("TCLS_ACCOUNT_PREVIEW");
            if (preview != null)
            {
                using var bitmap = new Bitmap(dialog.Width, dialog.Height);
                dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(Path.GetDirectoryName(preview)!, Path.GetFileNameWithoutExtension(preview) + "-" + theme + ".png"));
            }
            var region = Descendants(dialog).OfType<AntdUI.Select>().Single(control => control.Name == "tclsRegion");
            region.Text = "自定义大区";
            Descendants(dialog).OfType<AntdUI.Button>().Single(control => control.Text == "保存账号").PerformClick();
            Assert.Equal("自定义大区", store.Load().Single().Region);
            Assert.Equal("not-a-real-password", store.ReadPassword(account.Id));
            Assert.Empty(password.Text);
        }
        finally { LOL_GameAssistant.Helper.UiTheme.SetMode(previousTheme);if (File.Exists(path)) File.Delete(path); }
    });

    [Fact]
    public void SettingsExposeAccountSelectionAndPersistOptInWithoutStartingLogin() => MatchListScrollingTests.OnUiThread(() =>
    {
        string path = Path.Combine(Path.GetTempPath(), "tcls-settings-" + Guid.NewGuid() + ".json");
        try
        {
            var accounts = new LocalTclsAccountStore(path);
            var account = accounts.Save(null, "测试账号", "123456789", "艾欧尼亚", "fake-password");
            var saved = new AssistantSettings();int starts = 0;
            var settingsStore = SecondPhaseRegressionTests.Service<IApplicationSettingsStore>((method, args) =>
            {
                if (method.Name == "Load") return LegacySettingsMapper.ToDomain(LegacySettingsMapper.ToLegacy(saved));
                if (method.Name == "Save") { saved = (AssistantSettings)args[0]!;return null; }
                return "test-settings.json";
            });
            var launcher = SecondPhaseRegressionTests.Service<IGameClientLauncher>((method, _) =>
            { if (method.Name == "NormalizeConfiguredDirectory") return "";starts++;return null; });
            using var settings = new SettingForm(launcher, settingsStore,
                SecondPhaseRegressionTests.Service<ISettingsSecretProtector>((_, _) => null),
                SecondPhaseRegressionTests.Service<IClientFeatureService>((_, _) => null), accounts);
            using var host = new Form { ClientSize = new Size(1000, 720), ShowInTaskbar = false };
            settings.Dock = DockStyle.Fill;host.Controls.Add(settings);host.Show();System.Windows.Forms.Application.DoEvents();
            Descendants(settings).OfType<AntdUI.Segmented>().First().SelectIndex = 1;System.Windows.Forms.Application.DoEvents();
            var selection = Descendants(settings).OfType<AntdUI.Select>().Single(control => control.Name == "tclsSelectedAccount");
            Assert.Single(selection.Items.Cast<object>());Assert.Equal(account.Id, ((LOL_GameAssistant.Domain.LeagueClient.TclsAccount)selection.SelectedValue!).Id);
            var enabled = Descendants(settings).OfType<AntdUI.Checkbox>().Single(control => control.Name == "tclsAutoLogin");
            Assert.False(enabled.Checked);enabled.Checked = true;System.Windows.Forms.Application.DoEvents();
            Assert.True(saved.TclsAutoLoginEnabled);Assert.Equal(account.Id, saved.TclsSelectedAccountId);Assert.Equal(0, starts);
            string? preview = Environment.GetEnvironmentVariable("TCLS_SETTINGS_PREVIEW");
            if (preview != null)
            {
                using var bitmap = new Bitmap(host.Width, host.Height);
                host.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));bitmap.Save(preview);
            }
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    });

    [Fact]
    public async Task MissingProfileDoesNotStartClient()
    {
        string path = Path.Combine(Path.GetTempPath(), "tcls-missing-" + Guid.NewGuid() + ".json");
        var launcher = new TclsAutomaticClientLauncher(new LocalGameClientLauncher(),
            () => new AssistantSettings { TclsAutoLoginEnabled = true, TclsSelectedAccountId = "missing" }, new LocalTclsAccountStore(path));
        var result = await launcher.StartAndVerifyAsync("not-a-real-installation");
        Assert.False(result.Started);Assert.Contains("添加并选择", result.Message);
    }

    [Fact]
    public void NativeUiaFormFillsOnceSelectsRegionAndStarts() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = new LoginFixture();form.Show();System.Windows.Forms.Application.DoEvents();
        var session = new TclsLoginSession();
        var first = Step(form, session);
        Assert.False(first.Pause);Assert.True(session.Submitted);Assert.Equal(1, form.Logins);
        Assert.Equal("123456789", form.Username.Text);Assert.Equal(" fake-password ", form.Password.Text);
        Step(form, session);Assert.True(session.RegionSelected);
        Step(form, session);Assert.True(session.Launched);Assert.Equal(1, form.Starts);
        Step(form, session);Assert.Equal(1, form.Logins);Assert.Equal(1, form.Starts);
    });

    [Fact]
    public void LoginIsNotResubmittedWhileWaitingForResponse() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = new LoginFixture { ShowRegionsAfterLogin = false };form.Show();System.Windows.Forms.Application.DoEvents();
        var session = new TclsLoginSession();Step(form, session);Step(form, session);Step(form, session);
        Assert.Equal(1, form.Logins);Assert.Equal(0, form.Starts);
    });

    [Fact]
    public void UnmatchedRegionNeverStartsGame() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = new LoginFixture();
        form.Controls.OfType<RadioButton>().Single().Text = "祖安";
        form.Show();System.Windows.Forms.Application.DoEvents();
        var session = new TclsLoginSession();Step(form, session);Step(form, session);Step(form, session);
        Assert.False(session.RegionSelected);Assert.False(session.Launched);Assert.Equal(0, form.Starts);
    });

    [Fact]
    public void VerificationAndAmbiguousFormsDoNotReceiveCredentials() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var form = new LoginFixture();form.Show();System.Windows.Forms.Application.DoEvents();
        var verification = new Label { Text = "请输入验证码", Location = new Point(10, 210), AutoSize = true };
        form.Controls.Add(verification);System.Windows.Forms.Application.DoEvents();
        Assert.True(Step(form, new()).Pause);Assert.Empty(form.Password.Text);Assert.Equal(0, form.Logins);
        form.Controls.Remove(verification);verification.Dispose();
        using var extra = new TextBox { Location = new Point(10, 210) };form.Controls.Add(extra);System.Windows.Forms.Application.DoEvents();
        Step(form, new());Assert.Empty(form.Password.Text);Assert.Equal(0, form.Logins);
        using var canceled = new CancellationTokenSource();canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => Step(form, new(), canceled.Token));Assert.Equal(0, form.Logins);
    });

    private static TclsLoginStep Step(Form form, TclsLoginSession session, CancellationToken token = default)
    {
        IntPtr handle = form.Handle;
        var task = Task.Run(() =>
        {
            using var automation = new UIA3Automation { ConnectionTimeout = TimeSpan.FromSeconds(2), TransactionTimeout = TimeSpan.FromSeconds(2) };
            return TclsLoginAutomation.Step([automation.FromHandle(handle)], session, "123456789", " fake-password ", "艾欧尼亚", token);
        });
        SecondPhaseRegressionTests.PumpUntil(() => task.IsCompleted);
        return task.GetAwaiter().GetResult();
    }

    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>()
        .SelectMany(control => new[] { control }.Concat(Descendants(control)));

    private sealed class LoginFixture : Form
    {
        internal TextBox Username = new() { Location = new Point(20, 20), AccessibleName = "账号" };
        internal TextBox Password = new() { Location = new Point(20, 60), AccessibleName = "密码", UseSystemPasswordChar = true };
        internal int Logins, Starts;
        internal bool ShowRegionsAfterLogin = true;
        internal LoginFixture()
        {
            ClientSize = new Size(450, 300);ShowInTaskbar = false;Text = "模拟 TCLS 登录窗口";
            var login = new Button { Text = "登录", Location = new Point(20, 100) };
            var region = new RadioButton { Text = "艾欧尼亚", Location = new Point(20, 140), Visible = false };
            var start = new Button { Text = "进入游戏", Location = new Point(20, 180), Visible = false };
            login.Click += (_, _) => { Logins++;if (ShowRegionsAfterLogin) { Username.Hide();Password.Hide();login.Hide();region.Show();start.Show(); } };
            start.Click += (_, _) => Starts++;
            Controls.AddRange([Username, Password, login, region, start]);
        }
    }
}
