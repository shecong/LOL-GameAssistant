using LOL_GameAssistant.Domain.Skins;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public sealed partial class SkinToolsForm
{
    private AntdUI.Panel? _objectCard;
    private readonly AntdUI.Select _minionSkins = new() { Dock = DockStyle.Top, Height = 52, List = true, MaxCount = 12, DropDownArrow = true };
    private readonly AntdUI.Select _wardSkins = new() { Dock = DockStyle.Top, Height = 52, List = true, MaxCount = 12, DropDownArrow = true, PlaceholderText = "读取后选择自己的眼位皮肤" };
    private readonly AntdUI.Label _objectStatus = new() { Dock = DockStyle.Top, Height = 64, Text = "读取当前小兵与自己的眼位。应用后每 2 秒同步本局新生成的对象；恢复或停用后停止。" };
    private readonly AntdUI.Button _objectsRead = new() { Text = "读取小兵与眼位", Width = 160, Height = 42 };
    private readonly AntdUI.Button _objectsApply = new() { Text = "应用对象皮肤", Width = 160, Height = 42 };
    private readonly AntdUI.Button _objectsRestore = new() { Text = "恢复对象原皮", Width = 160, Height = 42 };
    private SkinCoreReply? _objectCatalog;
    private bool _objectsBusy;
    private int _appliedMinionIndex = -1, _appliedWardSkin = -1;
    private static readonly string[] MinionSkinNames = ["默认小兵", "夏日小兵", "源计划小兵", "冰雪节小兵", "德莱文小兵", "星之守护者小兵", "电玩小兵", "冰雪节小兵 II", "奥德赛小兵", "鼠年小兵", "双城之战小兵"];
    private AntdUI.Panel CreateObjectCard()
    {
        _objectCard = new AntdUI.Panel { Dock = DockStyle.Fill, Radius = 14, Padding = new Padding(24), BorderWidth = 1, Margin = new Padding(0, 12, 0, 0) };
        _minionSkins.Items.Add("不修改小兵");
        foreach (var name in MinionSkinNames) _minionSkins.Items.Add(name);
        _minionSkins.SelectedIndex = 0;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 64, WrapContents = true, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        buttons.Controls.AddRange([_objectsRead, _objectsApply, _objectsRestore]);
        _objectCard.Controls.Add(_objectStatus); _objectCard.Controls.Add(buttons);
        _objectCard.Controls.Add(_wardSkins);
        _objectCard.Controls.Add(new AntdUI.Label { Dock = DockStyle.Top, Height = 28, Text = "眼位皮肤（仅自己的眼位）" });
        _objectCard.Controls.Add(_minionSkins);
        _objectCard.Controls.Add(new AntdUI.Label { Dock = DockStyle.Top, Height = 28, Text = "小兵主题" });
        _objectCard.Controls.Add(new AntdUI.Label { Dock = DockStyle.Top, Height = 48, Text = "小兵与眼位", Font = new Font(UiMetrics.FontFamily, 16, FontStyle.Bold) });
        _objectsRead.Click += async (_, _) => await RunObjectsAsync(0);
        _objectsApply.Click += async (_, _) => await RunObjectsAsync(1);
        _objectsRestore.Click += async (_, _) => await RunObjectsAsync(2);
        return _objectCard;
    }
    private void UpdateObjectButtons()
    {
        bool ready = _inProgress && _core.Enabled && Visible && !_busy && !_refreshing && !_objectsBusy && _pageRequests != null;
        _objectsRead.Enabled = ready;
        _objectsApply.Enabled = _objectsRestore.Enabled = ready && _objectCatalog != null;
        // A manual hero read must not close an open object selector.
        _minionSkins.Enabled = _wardSkins.Enabled = _inProgress && _core.Enabled && Visible && !_busy && !_objectsBusy;
    }
    private Task RunObjectsAsync(int action) => RunObjectSelectionAsync(action, null, null);
    private async Task RunObjectSelectionAsync(int action, int? minionOverride, int? wardOverride)
    {
        if (_disposing || !_inProgress || _objectsBusy || _busy || _refreshing || !_core.Enabled || _pageRequests == null) return;
        long revision = Interlocked.Read(ref _revision); var token = _pageRequests.Token;
        _objectsBusy = _busy = true; UpdateButtons();
        try {
            SkinCoreReply result;
            if (action == 0) {
                int oldWardIndex = _wardSkins.SelectedIndex - 1;
                int previousWard = _objectCatalog != null && oldWardIndex >= 0 && oldWardIndex < _objectCatalog.WardEntries.Count ? _objectCatalog.WardEntries[oldWardIndex].SkinNum : -1;
                string? previousSession = _objectCatalog?.Session;
                result = await _core.GetObjectCatalogAsync(token);
                if (IsDisposed || revision != Interlocked.Read(ref _revision)) return;
                if (previousSession != null && previousSession != result.Session) ResetObjectSelection();
                _objectCatalog = result;
                _wardSkins.Items.Clear(); _wardSkins.Items.Add("不修改眼位");
                foreach (var entry in result.WardEntries) _wardSkins.Items.Add($"{entry.Name} · #{entry.SkinNum}");
                _wardSkins.SelectedIndex = previousSession == result.Session && previousWard >= 0
                    ? result.WardEntries.ToList().FindIndex(e => e.SkinNum == previousWard) + 1 : 0;
                _objectStatus.Text = $"当前小兵 {result.LaneCount} · 自己的眼位 {result.OwnWardCount} · 眼位皮肤 {result.WardEntries.Count}";
            } else {
                if (_objectCatalog == null) return;
                int wardIndex = _wardSkins.SelectedIndex - 1;
                int ward = wardOverride ?? (wardIndex >= 0 && wardIndex < _objectCatalog.WardEntries.Count ? _objectCatalog.WardEntries[wardIndex].SkinNum : -1);
                int minion = minionOverride ?? (_minionSkins.SelectedIndex - 1);
                result = action == 2 ? await _core.RestoreObjectsAsync(_objectCatalog.Session, token) :
                    await _core.ApplyObjectsAsync(_objectCatalog.Session, minion, ward, token);
                if (IsDisposed || revision != Interlocked.Read(ref _revision)) return;
                if (action == 2 && result.ObjectTargetCount == 0 || result.StateVerified)
                {
                    if (action == 2) ResetObjectSelection();
                    else { _appliedMinionIndex = minion; _appliedWardSkin = ward; }
                }
                _objectStatus.Text = action == 2 && result.ObjectTargetCount == 0 && !result.StateVerified
                    ? "当前没有目标对象，已停止自动同步；原皮恢复尚未核验。" : result.StateVerified ? $"状态已核验：对象 {result.ObjectTargetCount}，本次刷新 {result.ObjectChangedCount}。" +
                    (action == 2 ? "已停止自动同步。" : "已开启本局新对象自动同步，请检查游戏画面。") :
                    "没有可核验的目标对象，或读取状态未通过；未标记为成功。";
            }
        } catch (OperationCanceledException) { }
        catch (Exception error) {
            if (!IsDisposed && revision == Interlocked.Read(ref _revision)) _objectStatus.Text = error is SkinCoreException core && core.Code == "object_baseline_unavailable"
                ? "尚无目标对象或原皮编号不一致。请保留小兵、放置自己的眼位后重新读取。" : $"对象换肤未完成：{error.Message}";
        } finally { _objectsBusy = _busy = false; if (!IsDisposed) UpdateButtons(); }
    }
    private void ResetObjectSelection()
    {
        _appliedMinionIndex = _appliedWardSkin = -1;
        _minionSkins.SelectedIndex = 0;
        _wardSkins.SelectedIndex = _wardSkins.Items.Count == 0 ? -1 : 0;
    }
}
