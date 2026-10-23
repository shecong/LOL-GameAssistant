using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Domain.Skins;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public sealed partial class SkinToolsForm
{
    private readonly AntdUI.Select _coreMode = new() { Name = "SkinCoreMode", Dock = DockStyle.Top, Height = 42,
        List = true, DropDownArrow = true };
    private readonly AntdUI.Label _coreModeHint = new() { Name = "SkinCoreModeHint", Dock = DockStyle.Top,
        Height = 52, TextMultiLine = true };
    private bool _updatingCoreMode;

    private Control CreateCoreModeRow()
    {
        var row = new Panel { Dock = DockStyle.Top, Height = 120 };
        _coreMode.Items.Add(UiLanguage.T("经典模式（原有核心）"));
        _coreMode.Items.Add(UiLanguage.T("混合模式（内核通信 + 进程内执行器）"));
        _coreMode.SelectedIndex = _core is ISkinCoreModeSelector selector ? (int)selector.Mode : 0;
        row.Controls.Add(_coreModeHint);
        row.Controls.Add(_coreMode);
        row.Controls.Add(new AntdUI.Label { Dock = DockStyle.Top, Height = 26, Text = UiLanguage.T("换肤方式") });
        _surfaceRows.Add(row);
        _coreMode.SelectedIndexChanged += (_, _) => ChangeCoreMode();
        return row;
    }

    private void UpdateCoreModeControls()
    {
        _coreMode.Enabled = _core is ISkinCoreModeSelector && !_inProgress && !_core.Enabled && !_busy && !_refreshing && !_cameraBusy;
        _coreModeHint.Text = UiLanguage.T(_coreMode.SelectedIndex == 1
            ? "实验模式：需要已签名并启动的配套驱动及管理员权限。模型和特效仍由进程内执行器更新。退出对局并关闭功能后可切换。"
            : "使用保留的原有通信与换肤核心。关闭功能且退出对局后可切换使用方式。");
    }

    private void ChangeCoreMode()
    {
        if (_updatingCoreMode || _core is not ISkinCoreModeSelector selector) return;
        var previous = selector.Mode;
        try
        {
            if (!_coreMode.Enabled || _coreMode.SelectedIndex is < 0 or > 1) throw new SkinCoreException("core_mode_busy");
            var selected = (SkinCoreMode)_coreMode.SelectedIndex;
            selector.SetMode(selected);
            try
            {
                var settings = _settings.Load(); settings.SkinCoreMode = selected; _settings.Save(settings);
            }
            catch { selector.SetMode(previous); throw; }
            ResetSession();
            _rememberedSessions.Clear();
            _status.Text = UiLanguage.T(selected == SkinCoreMode.Classic ? "已选择经典模式，启用后读取当前对局。" : "已选择混合模式，启用后检查驱动通道；不会自动回退到经典模式。");
        }
        catch (Exception error)
        {
            _updatingCoreMode = true;
            try { _coreMode.SelectedIndex = (int)selector.Mode; }
            finally { _updatingCoreMode = false; }
            _status.Text = UiLanguage.T("使用方式切换失败：") + error.Message;
        }
        UpdateCoreModeControls();
    }
}
