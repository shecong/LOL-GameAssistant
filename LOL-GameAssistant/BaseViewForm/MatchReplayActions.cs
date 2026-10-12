using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>使用当前战绩的对局 ID 下载或观看客户端回放。</summary>
internal sealed class MatchReplayActions : UserControl
{
    private readonly long _gameId;
    private readonly IClientFeatureService _features;
    private readonly UiRequestScope _requests = new();
    private readonly AntdUI.Button _download;
    private readonly AntdUI.Button _watch;
    private readonly ToolTip _tips = new();
    private bool _busy;

    internal MatchReplayActions(long gameId, IClientFeatureService? features = null)
    {
        _gameId = gameId;
        _features = features ?? AppCompositionRoot.ClientFeatureService;
        Size = new Size(176, 32);
        BackColor = Color.Transparent;
        _download = new AntdUI.Button { Text = UiLanguage.T("下载回放"), Size = new Size(84, 32) };
        _watch = new AntdUI.Button { Text = UiLanguage.T("观看回放"), Size = new Size(84, 32), Location = new Point(92, 0) };
        Controls.AddRange([_download, _watch]);
        _download.Enabled = _watch.Enabled = gameId > 0;
        _tips.SetToolTip(_download, UiLanguage.T("请求客户端开始下载此对局的回放文件。"));
        _tips.SetToolTip(_watch, UiLanguage.T("请求客户端启动已下载完成的回放。"));
        _download.Click += async (_, _) => await RunAsync(false);
        _watch.Click += async (_, _) => await RunAsync(true);
    }

    internal async Task RunAsync(bool watch)
    {
        if (_busy || IsDisposed || _gameId <= 0) return;
        _busy = true;
        _download.Enabled = _watch.Enabled = false;
        AntdUI.Button active = watch ? _watch : _download;
        active.Loading = true;
        try
        {
            ClientFeatureResult result = watch
                ? await _features.WatchReplayAsync(_gameId, _requests.Token)
                : await _features.DownloadReplayAsync(_gameId, _requests.Token);
            if (IsDisposed || _requests.Token.IsCancellationRequested) return;
            if (FindForm() is { } form)
            {
                if (result.Succeeded) UiMessage.success(form, result.Message);
                else UiMessage.warn(form, result.Message);
            }
        }
        catch (OperationCanceledException) when (_requests.Token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!IsDisposed && FindForm() is { } form)
                UiMessage.error(form, watch ? "启动回放失败，请先下载完整回放" : "下载回放失败");
        }
        finally
        {
            _busy = false;
            if (!IsDisposed)
            {
                active.Loading = false;
                _download.Enabled = _watch.Enabled = _gameId > 0;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _requests.Dispose(); _tips.Dispose(); }
        base.Dispose(disposing);
    }
}
