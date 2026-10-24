using LOL_GameAssistant.Application.ApplicationInfo;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.ApplicationInfo;

namespace LOL_GameAssistant.Helper;

/// <summary>启动和手动检查共用的可选更新提示，不下载或替换本地文件。</summary>
internal static class AppUpdateChecker
{
    private static bool _checking;
    private static UpdateAvailableForm? _notification;
    internal static UpdateRelease? AvailableRelease { get; private set; }
    internal static event EventHandler? AvailabilityChanged;

    internal static void SetAvailableRelease(UpdateRelease? release)
    {
        var available = release != null && AppVersion.IsNewer(release.Version, AppVersion.Current) ? release : null;
        if (AvailableRelease == available) return;
        AvailableRelease = available;
        AvailabilityChanged?.Invoke(null, EventArgs.Empty);
    }

    internal static void ShowAvailable(Form owner, Action<string>? openUrl = null)
    {
        if (owner.IsDisposed || owner.Disposing || AvailableRelease == null) return;
        if (_notification is { IsDisposed: false })
        {
            _notification.Activate();
            return;
        }
        _notification = new UpdateAvailableForm(AvailableRelease, openUrl);
        _notification.Show(owner);
    }

    public static async Task CheckAsync(Form owner, IUpdateReleaseService service, bool automatic,
        Action<string>? openUrl = null)
    {
        if (_checking || owner.IsDisposed || owner.Disposing) return;
        if (_notification is { IsDisposed: false })
        {
            if (!automatic) _notification.Activate();
            return;
        }

        _checking = true;
        try
        {
            UpdateRelease? release = await service.GetLatestAsync();
            if (owner.IsDisposed || owner.Disposing) return;
            if (release == null)
            {
                if (!automatic) UiMessage.warn(owner, "未获取到版本信息");
                return;
            }

            SetAvailableRelease(release);
            if (AvailableRelease != null)
            {
                ShowAvailable(owner, openUrl);
            }
            else if (!automatic)
            {
                UiMessage.success(owner, $"当前已是最新版本 ({AppVersion.Current.ToString(3)})");
            }
        }
        catch (Exception ex)
        {
            RuntimeDiagnostics.Report("软件更新", "检查失败", ex.Message);
            if (!automatic && !owner.IsDisposed && !owner.Disposing)
                UiMessage.error(owner, $"暂时无法检查更新，请稍后重试，或前往服务器下载页 / GitHub 发布页 / QQ 群 {UpdateAvailableForm.QqGroupNumber} 获取。详情见日志。");
        }
        finally
        {
            _checking = false;
        }
    }
}
