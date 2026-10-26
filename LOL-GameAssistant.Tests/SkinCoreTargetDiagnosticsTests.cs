using System.Diagnostics;
using LOL_GameAssistant.Domain.Skins;
using LOL_GameAssistant.Infrastructure.Skins;
using Xunit;

namespace LOL_GameAssistant.Tests;

public sealed class SkinCoreTargetDiagnosticsTests
{
    [Fact]
    public void MissingModuleAfterCleanupDoesNotProveBlocking()
    {
        var probe = new SkinCoreTargetProbe { TargetRunning = true, TargetArchitecture = "x64",
            AssistantElevated = false, TargetElevated = false, WindowHung = false, CoreModuleLoadedAfterCleanup = false };
        string assessment = SkinCoreTargetDiagnostics.Assess(probe, "x64", 0);
        Assert.Contains("不能区分", assessment);
        Assert.Contains("不代表此前从未加载", assessment);
    }

    [Fact]
    public void UnknownChecksDoNotBecomeArchitectureOrPermissionFailures()
    {
        string assessment = SkinCoreTargetDiagnostics.Assess(new(), "x64", 0);
        Assert.Contains("不能区分", assessment);
        Assert.DoesNotContain("位数不一致", assessment);
        Assert.DoesNotContain("游戏已提权", assessment);
    }

    [Theory]
    [InlineData("x86", "x64")]
    [InlineData("x64", "x86")]
    public void ConfirmedBitnessMismatchHasSpecificRemedy(string target, string host) =>
        Assert.Contains("位数不一致", SkinCoreTargetDiagnostics.Assess(new() { TargetArchitecture = target }, host, 0));

    [Fact]
    public void ConfirmedElevationMismatchHasSpecificRemedy() =>
        Assert.Contains("权限级别一致", SkinCoreTargetDiagnostics.Assess(new() { TargetElevated = true, AssistantElevated = false }, "x64", 0));

    [Fact]
    public void HungAndExitedTargetsHaveSpecificRemedies()
    {
        Assert.Contains("未响应", SkinCoreTargetDiagnostics.Assess(new() { WindowHung = true }, "x64", 0));
        Assert.Contains("进程已退出", SkinCoreTargetDiagnostics.Assess(new() { TargetRunning = false }, "x64", 0));
    }

    [Fact]
    public void WindowsProbeCanReadCurrentProcessMetadataWithoutGameWindow()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var process = Process.GetCurrentProcess();
        var probe = SkinCoreTargetDiagnostics.Capture(new() { Pid = process.Id, ThreadId = 0, Phase = 0 });
        Assert.True(probe.TargetRunning);
        Assert.NotEmpty(probe.TargetArchitecture);
        Assert.NotNull(probe.AssistantElevated);
        Assert.NotNull(probe.TargetElevated);
        Assert.Null(probe.WindowHung);
        Assert.Contains("target_window_changed_or_missing", probe.Errors);
    }
}
