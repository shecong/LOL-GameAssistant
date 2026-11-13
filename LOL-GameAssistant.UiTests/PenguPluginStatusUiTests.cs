using System.Diagnostics;
using System.Reflection;
using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Application.Insights;
using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.BaseViewForm;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class PenguPluginStatusUiTests
{
    private sealed class Plugin(bool fail = false) : IPenguPluginService
    {
        public int Checks;
        public string PluginsDirectory => "";
        public PenguPluginStatus Inspect() => new(true, true, false, "原来的状态");
        public PenguPluginStatus Check()
        {
            Checks++;
            if (fail) throw new IOException("测试目录不可读取");
            return new(true, true, false, "插件文件：已安装\nLoader 模块：未在客户端进程中检测到\n请重启客户端。");
        }
        public void SelectDirectory(string directory) => throw new NotSupportedException();
        public void Install() => throw new NotSupportedException();
        public void SetEnabled(bool enabled) => throw new NotSupportedException();
        public void Uninstall() => throw new NotSupportedException();
    }

    public class ServiceProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name == "ListGameSettingsBackupsAsync"
            ? Task.FromResult<IReadOnlyList<ClientSettingsBackup>>([]) : null;
    }

    [Fact]
    public void CheckButtonOpensVisibleResultAndReusesItsWindow() => MatchListScrollingTests.OnUiThread(() =>
    {
        var plugin = new Plugin();
        using var tools = new ClientToolsForm(DispatchProxy.Create<IClientFeatureService, ServiceProxy>(),
            DispatchProxy.Create<IChampionInsightsService, ServiceProxy>(), DispatchProxy.Create<IProfileIconService, ServiceProxy>(), penguPlugin: plugin);
        using var host = new Form { ClientSize = new Size(1100, 760), ShowInTaskbar = false };
        host.Controls.Add(tools);
        host.Show();
        var check = Children(tools).OfType<AntdUI.Button>().Single(button => button.Text == "检查加载状态");
        Click(check);
        var window = Assert.Single(host.OwnedForms.OfType<PenguPluginStatusForm>());
        Assert.True(window.Visible);
        var details = Children(window).OfType<AntdUI.Input>().Single(control => control.Name == "penguCheckDetails");
        WaitFor(() => details.Text.Contains("检查完成"));
        Assert.Contains("Loader 模块：未在客户端进程中检测到", details.Text);
        Assert.Equal(1, plugin.Checks);
        Click(check);
        Assert.Same(window, Assert.Single(host.OwnedForms.OfType<PenguPluginStatusForm>()));
        string? preview = Environment.GetEnvironmentVariable("PENGU_CHECK_PREVIEW");
        if (!string.IsNullOrEmpty(preview))
        {
            using var bitmap = new Bitmap(window.Width, window.Height);
            window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(preview);
        }
        window.Close();
    });

    [Fact]
    public void ReadFailureIsDisplayedInsteadOfSilentlyRefreshingOldText() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var window = new PenguPluginStatusForm(new Plugin(fail: true), _ => { });
        window.Show();
        var details = Children(window).OfType<AntdUI.Input>().Single();
        WaitFor(() => details.Text.Contains("检查失败"));
        Assert.Contains("测试目录不可读取", details.Text);
        Assert.True(Children(window).OfType<AntdUI.Button>().Single(button => button.Name == "refreshPenguCheck").Enabled);
        window.Close();
    });

    private static IEnumerable<Control> Children(Control parent) => parent.Controls.Cast<Control>()
        .SelectMany(child => new[] { child }.Concat(Children(child)));
    private static void Click(Control control) => control.GetType().GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(control, [EventArgs.Empty]);
    private static void WaitFor(Func<bool> ready)
    {
        var clock = Stopwatch.StartNew();
        while (!ready() && clock.Elapsed < TimeSpan.FromSeconds(5)) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(10); }
        Assert.True(ready(), "检查窗口未显示结果。");
    }
}
