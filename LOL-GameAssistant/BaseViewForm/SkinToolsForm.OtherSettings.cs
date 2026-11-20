using Panel = AntdUI.Panel;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Domain.Skins;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public sealed partial class SkinToolsForm
{
    private AntdUI.Panel? _otherSettingsCard;
    private readonly AntdUI.Switch _cameraEnabled = new() { Name = "UnlimitedZoomEnabled", Width = 52, Height = 30, AccessibleName = "无限视距" };
    private readonly AntdUI.Label _cameraDistance = new() { Name = "UnlimitedZoomDistance", Dock = DockStyle.Top,
        Height = 48, Text = "当前缩放上限：进入对局后读取默认值" };
    private decimal _cameraDistanceValue;
    private readonly AntdUI.Label _cameraStatus = new() { Name = "UnlimitedZoomStatus", Dock = DockStyle.Top, Height = 96, TextMultiLine = true };
    private readonly AntdUI.Button _cameraRead = new() { Name = "CameraRead", Text = "重新读取视距", Width = 150, Height = 42 };
    private readonly AntdUI.Button _cameraRestore = new() { Name = "CameraRestore", Text = "关闭并恢复视距", Width = 150, Height = 42 };
    private readonly System.Windows.Forms.Timer _cameraWatch = new() { Interval = 100 };
    private readonly IGameOverlayBridge _cameraOverlay;
    private SkinCoreReply? _cameraReply;
    private bool _cameraBusy, _updatingCamera, _cameraActive, _cameraFaulted;
    private long _nextCameraRead;
    private int _cameraWheelRemainder;

    private Control CreateOtherSettingsPage()
    {
        var page = new Panel { Name = "OtherSkinSettingsPage", Dock = DockStyle.Fill, AutoScroll = true,
            Padding = new Padding(12), Visible = false };
        _otherSettingsCard = new AntdUI.Panel { Dock = DockStyle.Top, Height = 520, Radius = 14,
            Padding = new Padding(24), BorderWidth = 1 };
        var row = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, WrapContents = false };
        row.Controls.Add(new AntdUI.Label { Text = "无限视距", Width = 180, Height = 32 });
        row.Controls.Add(_cameraEnabled);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 64, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
        buttons.Controls.AddRange([_cameraRead, _cameraRestore]);
        _surfaceRows.AddRange([row, buttons]);
        _otherSettingsCard.Controls.Add(_cameraStatus);
        _otherSettingsCard.Controls.Add(buttons);
        _otherSettingsCard.Controls.Add(new AntdUI.Label { Dock = DockStyle.Top, Height = 80, TextMultiLine = true,
            Text = "开启时保留当前画面和缩放上限。滚轮每格调整上限 18（600–1700），关闭时恢复开启前的视距和缩放上限。滚轮中键可恢复游戏默认视距，Home 面板可切换开关。" });
        _otherSettingsCard.Controls.Add(_cameraDistance);
        _otherSettingsCard.Controls.Add(row);
        _otherSettingsCard.Controls.Add(new AntdUI.Label { Text = "其他设置", Dock = DockStyle.Top, Height = 48,
            Font = new Font(UiMetrics.FontFamily, 16, FontStyle.Bold) });
        page.Controls.Add(_otherSettingsCard);
        _cameraRead.Click += async (_, _) => { _cameraFaulted = false; await RunCameraAsync(0); };
        _cameraRestore.Click += async (_, _) => await SetCameraEnabledAsync(false);
        _cameraEnabled.CheckedChanged += async (_, _) =>
        {
            if (!_updatingCamera) await SetCameraEnabledAsync(_cameraEnabled.Checked);
        };
        return page;
    }

    private void InitializeCameraAutomation()
    {
        var settings = _settings.Load();
        SetCameraToggle(settings.UnlimitedZoomEnabled);
        _cameraStatus.Text = settings.UnlimitedZoomEnabled ? "已开启，进入对局后以当前视距为起点。" : "可在游戏前开启无限视距。";
        _cameraWatch.Tick += async (_, _) => await PollCameraAsync();
        _cameraWatch.Start();
    }

    private void UpdateCameraButtons()
    {
        bool idle = !_disposing && !_cameraBusy && !_busy && !_refreshing;
        bool ready = idle && _inProgress && _core.Enabled && _pageRequests != null;
        _cameraEnabled.Enabled = idle;
        _cameraRead.Enabled = ready;
        _cameraRestore.Enabled = idle && (_cameraEnabled.Checked || _cameraActive);
        if (_inProgress && _core.Enabled && _cameraEnabled.Checked && _cameraReply != null && !_cameraFaulted)
            _cameraOverlay.PublishPanel(new(GameOverlayPanelKind.Camera));
        else
        {
            _cameraOverlay.HidePanel(GameOverlayPanelKind.Camera);
            _cameraOverlay.ReadCameraWheel();
            _cameraOverlay.ReadCameraReset();
            _cameraWheelRemainder = 0;
        }
    }

    private void SetCameraToggle(bool enabled)
    {
        _updatingCamera = true;
        try { _cameraEnabled.Checked = enabled; }
        finally { _updatingCamera = false; }
    }

    internal async Task SetCameraEnabledAsync(bool enabled)
    {
        if (_disposing || _cameraBusy || _busy || _refreshing) { SetCameraToggle(_settings.Load().UnlimitedZoomEnabled); return; }
        bool previous = _settings.Load().UnlimitedZoomEnabled;
        try
        {
            _settings.Update(settings =>
            {
                settings.UnlimitedZoomEnabled = enabled;
                if (enabled) settings.SkinCoreEnabled = true;
            });
            SetCameraToggle(enabled);
            _cameraFaulted = false;
            if (enabled && !_cameraActive) _cameraReply = null;
            if (enabled && !_enabled.Checked) _enabled.Checked = true;
            if (_inProgress && _core.Enabled) await RunCameraAsync(0);
            else _cameraStatus.Text = enabled ? "已开启，进入对局后以当前视距为起点。" : "已关闭，下一局不会启用。";
        }
        catch (Exception error)
        {
            SetCameraToggle(previous);
            _cameraStatus.Text = "视距设置保存失败：" + error.Message;
        }
        finally { UpdateButtons(); }
    }

    private void ResetCameraSelection()
    {
        _cameraReply = null; _cameraActive = false; _cameraFaulted = false; _nextCameraRead = 0;
        _cameraWheelRemainder = 0;
        _cameraDistance.Text = "当前缩放上限：进入对局后读取";
        _cameraOverlay.ReadCameraWheel();
        _cameraStatus.Text = _cameraEnabled.Checked ? "已开启，等待下一局自动连接。" : "可在游戏前开启无限视距。";
    }

    internal async Task PollCameraAsync()
    {
        if (_disposing || !_inProgress || !_core.Enabled || !_cameraEnabled.Checked || _cameraFaulted ||
            _cameraBusy || _busy || _refreshing || _pageRequests == null) return;
        if (_cameraReply == null)
        {
            if (Environment.TickCount64 < _nextCameraRead) return;
            _nextCameraRead = Environment.TickCount64 + 2000;
            await RunCameraAsync(0);
            return;
        }
        if (_cameraOverlay.ReadCameraReset())
        {
            await RestoreDefaultCameraAsync();
            return;
        }
        await AdjustCameraWheelAsync(_cameraOverlay.ReadCameraWheel());
    }

    internal async Task RestoreDefaultCameraAsync()
    {
        if (_disposing || !_cameraEnabled.Checked || _cameraFaulted || _cameraReply == null || !_inProgress || !_core.Enabled ||
            _cameraBusy || _busy || _refreshing || _pageRequests == null) return;
        _cameraWheelRemainder = 0;
        _cameraOverlay.ReadCameraWheel();
        long revision = Interlocked.Read(ref _revision);
        var token = _pageRequests.Token;
        _cameraBusy = true; UpdateButtons();
        try
        {
            var result = await _core.RestoreCameraAsync(_cameraReply.Session, token);
            if (_disposing || revision != Interlocked.Read(ref _revision)) return;
            if (!result.StateVerified || result.CameraViewDistance != result.CameraDefaultViewDistance || result.CameraViewTarget != result.CameraDefaultViewDistance)
                throw new SkinCoreException("camera_view_write_not_verified", true);
            _cameraReply = result; _cameraDistanceValue = (decimal)result.CameraDistance;
            _cameraActive = true;
            _cameraDistance.Text = $"当前缩放上限：{result.CameraDistance:0.##}";
            _cameraStatus.Text = $"已恢复游戏默认视距 {result.CameraDefaultViewDistance:0.##}，无限视距仍开启。";
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (_disposing || revision != Interlocked.Read(ref _revision)) return;
            _cameraFaulted = true;
            _cameraStatus.Text = "当前视距调整未确认：" + error.Message + "。请重新读取后再操作。";
        }
        finally { _cameraBusy = false; if (!_disposing) UpdateButtons(); }
    }

    internal async Task AdjustCameraWheelAsync(int delta)
    {
        if (!_cameraEnabled.Checked || _cameraFaulted || _cameraReply == null || !_inProgress || !_core.Enabled) return;
        _cameraWheelRemainder += Math.Clamp(delta, -12000, 12000);
        if (_cameraBusy || _busy || _refreshing) return;
        int steps = _cameraWheelRemainder / 120;
        _cameraWheelRemainder %= 120;
        if (steps == 0) return;
        decimal target = Math.Clamp(_cameraDistanceValue - steps * 18, 600, 1700);
        if (target == _cameraDistanceValue) return;
        _cameraDistanceValue = target;
        await RunCameraAsync(1);
    }

    private async Task RunCameraAsync(int action)
    {
        if (_disposing || !_inProgress || !_core.Enabled || _busy || _refreshing || _cameraBusy || _pageRequests == null) return;
        long revision = Interlocked.Read(ref _revision);
        var token = _pageRequests.Token;
        _cameraBusy = true; UpdateButtons();
        try
        {
            bool enabled = _cameraEnabled.Checked;
            bool restore = !enabled && _cameraActive && _cameraReply != null;
            var result = restore
                ? await _core.EndCameraAsync(_cameraReply!.Session, token)
                : enabled && !_cameraActive ? await _core.BeginCameraAsync(token) : await _core.GetCameraAsync(token);
            if (_disposing || revision != Interlocked.Read(ref _revision)) return;
            if (!result.StateVerified) throw new SkinCoreException("camera_write_not_verified", true);
            if (enabled && _cameraActive && _cameraReply?.Session != result.Session)
            {
                _cameraActive = false;
                _cameraReply = null;
                return;
            }
            _cameraActive = enabled;
            _cameraReply = result;
            if (enabled && action == 1 && (float)_cameraDistanceValue != result.CameraDistance)
                result = await _core.SetCameraAsync(result.Session, (float)_cameraDistanceValue, token);
            if (_disposing || revision != Interlocked.Read(ref _revision)) return;
            if (!result.StateVerified) throw new SkinCoreException("camera_write_not_verified", true);
            _cameraReply = result; _cameraDistanceValue = (decimal)result.CameraDistance;
            _cameraDistance.Text = $"当前缩放上限：{result.CameraDistance:0.##}";
            _cameraStatus.Text = enabled
                ? $"已开启 · 当前缩放上限 {result.CameraDistance:0.##}，滚轮平缓调节，中键恢复游戏默认视距。"
                : restore ? $"已关闭，已恢复开启前的视距 {result.CameraViewDistance:0.##} 和缩放上限。"
                    : "已关闭。";
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (_disposing || revision != Interlocked.Read(ref _revision)) return;
            // Loading may precede camera availability; read-only discovery can retry.
            _cameraFaulted = error is not SkinCoreException { Code: "camera_unavailable" or "camera_view_unavailable" or "game_window_not_found" };
            _cameraStatus.Text = "视距操作未完成：" + error.Message + "。可重新读取后重试。";
        }
        finally { _cameraBusy = false; if (!_disposing) UpdateButtons(); }
    }
}
