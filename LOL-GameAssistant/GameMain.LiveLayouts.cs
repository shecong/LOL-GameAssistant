using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant;

public partial class GameMain
{
    private readonly AntdUI.Select _liveLayoutSelector = new()
    {
        Name = "liveLayoutSelector", List = true, DropDownArrow = true,
        AccessibleName = "对局布局", Radius = 6,
        Font = new Font(UiMetrics.FontFamily, 9)
    };
    private bool _restoringLiveLayout;

    private void ConfigureLiveLayoutSelector()
    {
        _liveLayoutSelector.Items.AddRange(new object[] {
            "布局：经典卡片", "布局：双队总览", "布局：位置对位", "布局：详情工作台", "布局：紧凑卡片"
        });
        var stored = _settingsStore.Load().LiveGameLayout;
        _liveLayoutSelector.SelectedIndex = (int)(Enum.IsDefined(stored) ? stored : LiveGameLayout.Classic);
        liveGameForm.SetDisplayLayout((LiveGameLayout)_liveLayoutSelector.SelectedIndex);
        var settingsButton = new AntdUI.Button
        {
            Name = "liveGameSettings", Text = "设置", AccessibleName = "对局设置",
            Width = 76, Dock = DockStyle.Right, Radius = 6
        };
        settingsButton.Click += (_, _) =>
        {
            using var dialog = new LiveGameSettingsForm(_settingsStore.Load().LiveHistoryFilter, filter =>
            {
                var latest = _settingsStore.Load();
                latest.LiveHistoryFilter = filter;
                _settingsStore.Save(latest);
                liveGameForm.SetHistoryFilter(filter);
            });
            dialog.ShowDialog(this);
        };
        liveGameForm.SetHistoryFilter(_settingsStore.Load().LiveHistoryFilter);
        liveGameForm.AttachLayoutSelector(_liveLayoutSelector, settingsButton);
        _liveLayoutSelector.SelectedIndexChanged += (_, _) =>
        {
            if (_restoringLiveLayout || _liveLayoutSelector.SelectedIndex < 0) return;
            var previous = liveGameForm.DisplayLayout;
            try
            {
                var latest = _settingsStore.Load();
                latest.LiveGameLayout = (LiveGameLayout)_liveLayoutSelector.SelectedIndex;
                _settingsStore.Save(latest);
                liveGameForm.SetDisplayLayout(latest.LiveGameLayout);
            }
            catch (Exception error)
            {
                _restoringLiveLayout = true;
                try { _liveLayoutSelector.SelectedIndex = (int)previous; }
                finally { _restoringLiveLayout = false; }
                RuntimeDiagnostics.WriteException(error);
                UiMessage.error(this, $"布局切换失败：{error.Message}");
            }
        };
        _liveLayoutSelector.BackColor = UiTheme.Palette.SurfaceRaised;
        _liveLayoutSelector.ForeColor = UiTheme.Palette.TextPrimary;
        _liveLayoutSelector.Visible = IsLiveGameTabActive;
        tabs1.SelectedIndexChanged += (_, _) => _liveLayoutSelector.Visible = IsLiveGameTabActive;
    }
}
