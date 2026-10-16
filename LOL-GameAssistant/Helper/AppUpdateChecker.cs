using LOL_GameAssistant.Application.ApplicationInfo;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.ApplicationInfo;

namespace LOL_GameAssistant.Helper;

/// <summary>启动和手动检查共用的可选更新提示，不下载或替换本地文件。</summary>
internal static class AppUpdateChecker
{
    private static bool _checking;
    private static UpdateAvailableForm? _notification;

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

            if (AppVersion.IsNewer(release.Version, AppVersion.Current))
            {
                _notification = new UpdateAvailableForm(release, openUrl);
                _notification.Show(owner);
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
                UiMessage.error(owner, $"检查更新失败: {ex.Message}");
        }
        finally
        {
            _checking = false;
        }
    }
}
