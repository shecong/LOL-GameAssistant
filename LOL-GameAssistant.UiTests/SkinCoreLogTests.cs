using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Skins;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.Skins;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
public sealed class SkinCoreLogTests
{
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
        chat.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "清空消息").PerformClick();
        Assert.Empty(messages.GetLogText());
        Assert.Empty(chat.Items);
    });

    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>()
        .SelectMany(child => new[] { child }.Concat(Descendants(child)));
}
