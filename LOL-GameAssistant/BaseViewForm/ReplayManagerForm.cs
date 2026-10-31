using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

internal sealed class ReplayManagerForm : AntdUI.Window, IThemeAware
{
    private readonly IClientFeatureService _features;
    private readonly long _gameId;
    private readonly UiRequestScope _requests = new();
    private readonly AntdUI.Label _status = new() { Dock = DockStyle.Fill, TextMultiLine = true, Padding = new Padding(22), Text = "正在读取回放元数据…" };
    private readonly AntdUI.Button _download = new() { Text = "请求下载", AutoSize = true };
    private readonly AntdUI.Button _watch = new() { Text = "观看回放", AutoSize = true, Enabled = false };
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 2500 };
    private bool _busy;
    private DateTimeOffset _pollUntil;
    internal ReplayManagerForm(long gameId, IClientFeatureService features)
    {
        _features = features; _gameId = gameId; Text = $"对局回放 · {gameId}"; Size = new Size(570, 300); MinimumSize = new Size(450, 250); StartPosition = FormStartPosition.CenterParent;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(16) };
        var refresh = new AntdUI.Button { Text = "刷新状态", AutoSize = true }; buttons.Controls.AddRange([_download, _watch, refresh]); Controls.Add(_status); Controls.Add(buttons); AntdWindowChrome.AddHeader(this);
        refresh.Click += async (_, _) => { _pollUntil = DateTimeOffset.UtcNow.AddMinutes(3); await RefreshAsync(); };
        _download.Click += async (_, _) => await ExecuteAsync(false); _watch.Click += async (_, _) => await ExecuteAsync(true);
        _poll.Tick += async (_, _) => { if (DateTimeOffset.UtcNow >= _pollUntil || !Visible) _poll.Stop(); else await RefreshAsync(); };
        Shown += async (_, _) => { _pollUntil = DateTimeOffset.UtcNow.AddMinutes(3); await RefreshAsync(); };
        Disposed += (_, _) => { _poll.Dispose(); _requests.Dispose(); }; UiTheme.Apply(this);
    }
    private async Task RefreshAsync()
    {
        if (_busy || IsDisposed) return; _busy = true;
        try
        {
            var state = await _features.GetReplayStateAsync(_gameId, _requests.Token);
            if (IsDisposed) return;
            _status.Text = state.Message + "\n\n下载请求成功不代表文件已完成；关闭窗口后停止刷新。";
            _watch.Enabled = state.State == ReplayAvailability.Ready;
            _download.Enabled = state.State is ReplayAvailability.Downloadable or ReplayAvailability.Unknown;
            if (state.State == ReplayAvailability.Downloading && DateTimeOffset.UtcNow < _pollUntil) _poll.Start(); else _poll.Stop();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!IsDisposed) { _watch.Enabled = false; _status.Text = "状态读取失败：" + e.Message; } }
        finally { _busy = false; }
    }
    private async Task ExecuteAsync(bool watch)
    {
        if (_busy || IsDisposed) return; _busy = true; _download.Enabled = _watch.Enabled = false;
        try
        {
            var result = watch ? await _features.WatchReplayAsync(_gameId, _requests.Token) : await _features.DownloadReplayAsync(_gameId, _requests.Token);
            if (IsDisposed) return; _status.Text = result.Message;
            _pollUntil = DateTimeOffset.UtcNow.AddMinutes(3); _busy = false; await RefreshAsync();
            if (!watch && result.Succeeded && !IsDisposed) _poll.Start();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!IsDisposed) _status.Text = e.Message; }
        finally { _busy = false; }
    }
    public void ApplyTheme(ThemePalette p) { BackColor = p.Surface; _status.ForeColor = p.TextPrimary; }
}
