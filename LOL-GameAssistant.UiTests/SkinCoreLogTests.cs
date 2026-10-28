using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Skins;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.Skins;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class SkinCoreLogTests
{
    [Theory]
    [InlineData("no_verified_game_process", 0, "未检测到游戏对局进程")]
    [InlineData("no_verified_game_process", 1, "无法完成进程校验")]
    [InlineData("no_usable_game_window", 0, "已检测到游戏进程")]
    [InlineData("ambiguous_game_processes", 0, "多个候选游戏窗口")]
    public void WindowDiscoveryExplainsWhichStepFailed(string state, int queryErrors, string expected)
    {
        string reason = RuntimeDiagnostics.DescribeGameWindow(new() { State = state, QueryErrors = queryErrors, Win32 = 5 });
        Assert.Contains(expected, reason);
    }

    [Fact]
    public void AutomaticTargetChecksAreIncludedInCopiedSoftwareLog() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var messages = new InfoMsgForm();
        using var host = new Form { ShowInTaskbar = false };
        host.Controls.Add(messages);
        host.Show();
        string marker = Guid.NewGuid().ToString("N");
        RuntimeDiagnostics.ReportSkinCoreFailure(new SkinCoreException("core_response_timeout", details:
            new SkinCoreFailureDetails { Stage = "response_wait", Win32 = 1460, Phase = 0, RequestId = marker,
                TargetProbe = new SkinCoreTargetProbe { TargetArchitecture = "x64", TargetElevated = true,
                    AssistantElevated = false, Assessment = "游戏已提权、助手未提权，请保持两者权限级别一致后重新读取。" } }));
        SecondPhaseRegressionTests.PumpUntil(() => messages.GetLogText().Contains(marker));
        Assert.Contains("自动检查", messages.GetLogText());
        Assert.Contains("权限级别一致", messages.GetLogText());
        Assert.Contains("\"TargetArchitecture\":\"x64\"", messages.GetLogText());
        Assert.Contains("\"Phase\":0", messages.GetLogText());
    });

    [Fact]
    public void HookEntryFailureHasReadableExplanationInCopiedLog() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var messages = new InfoMsgForm();
        using var host = new Form { ShowInTaskbar = false };
        host.Controls.Add(messages);
        host.Show();
        string marker = Guid.NewGuid().ToString("N");
        RuntimeDiagnostics.ReportSkinCoreFailure(new SkinCoreException("core_response_timeout", details:
            new SkinCoreFailureDetails { RequestId = marker, Phase = 0, HookTraceAvailable = true,
                HookCallbacks = 4, HookMessages = 1, HookStage = "request_open_failed", HookWin32 = 5 }));
        SecondPhaseRegressionTests.PumpUntil(() => messages.GetLogText().Contains(marker));
        Assert.Contains("打开请求共享内存失败", messages.GetLogText());
        Assert.Contains("回调 4 次", messages.GetLogText());
        Assert.Contains("目标消息 1 次", messages.GetLogText());
        Assert.Contains("\"HookWin32\":5", messages.GetLogText());
    });

    [Fact]
    public void BackgroundTransportFailureAppearsInExistingSoftwareLog() => MatchListScrollingTests.OnUiThread(() =>
    {
        using var messages = new InfoMsgForm();
        using var host = new Form { ShowInTaskbar = false };
        host.Controls.Add(messages);
        host.Show();
        string marker = "skin-log-test-" + Guid.NewGuid();
        string missingDirectory = Path.Combine(Path.GetTempPath(), marker);
        var transport = new NativeSkinCoreTransport(missingDirectory, diagnostic: RuntimeDiagnostics.ReportSkinCoreFailure);
        var error = Task.Run(async () => await Assert.ThrowsAsync<SkinCoreException>(() =>
            transport.InvokeAsync(["catalog"], default))).GetAwaiter().GetResult();
        SecondPhaseRegressionTests.PumpUntil(() => messages.GetLogText().Contains(marker));
        Assert.Equal("core_files_missing", error.Code);
        Assert.Contains("核心运行文件缺失", messages.GetLogText());
        Assert.Contains("ProcessArchitecture", messages.GetLogText());
        Assert.Contains("OsArchitecture", messages.GetLogText());
        Assert.Contains(messages.Controls.OfType<AntdUI.Button>(), button => button.Name == "copyLogs" && button.Text == "复制日志");
        Assert.Contains(marker, File.ReadAllText(RuntimeDiagnostics.GetLogPath()));
        Assert.Contains(RuntimeDiagnostics.Snapshot(), entry => entry.Component == RuntimeDiagnostics.SkinCoreComponent
            && entry.Detail.Contains(marker));
    });

    [Fact]
    public void OpeningLogAfterFailureRecoversLatestDiagnosticAndAllowsClearing() => MatchListScrollingTests.OnUiThread(() =>
    {
        string requestId = Guid.NewGuid().ToString("N");
        RuntimeDiagnostics.ReportSkinCoreFailure(new SkinCoreException("message_post_failed", details:
            new SkinCoreFailureDetails { Stage = "message_post", Win32 = 5, RequestId = requestId }));
        using var messages = new InfoMsgForm();
        using var host = new Form { ShowInTaskbar = false };
        host.Controls.Add(messages);
        host.Show();
        SecondPhaseRegressionTests.PumpUntil(() => messages.GetLogText().Contains(requestId));
        Assert.Contains("拒绝访问", messages.GetLogText());
        Assert.Contains("\"Win32\":5", messages.GetLogText());
        string before = messages.GetLogText();
        RuntimeDiagnostics.Report("日志测试", "完成", "不应重复追加核心错误");
        Assert.Equal(before, messages.GetLogText());
        var chat = Descendants(messages).OfType<AntdUI.Chat.ChatList>().Single();
        ContextMenuTestDriver.Click(chat, "清空消息");
        Assert.Empty(messages.GetLogText());
        Assert.Empty(chat.Items);
    });

    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>()
        .SelectMany(child => new[] { child }.Concat(Descendants(child)));
}
