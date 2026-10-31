using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

internal sealed class BackupPreviewForm : AntdUI.Window
{
    private readonly UiRequestScope _requests = new();
    internal BackupPreviewForm(string name, IClientFeatureService features)
    {
        Text = "恢复预览 · " + name; Size = new Size(850, 590); MinimumSize = new Size(650, 450); StartPosition = FormStartPosition.CenterParent;
        var table = new AntdUI.Table { Dock = DockStyle.Fill, Bordered = true };
        foreach (var (key, title) in new[] { ("Category", "类别"), ("Key", "设置组"), ("Current", "当前值"), ("Saved", "备份值") }) table.Columns.Add(new AntdUI.Column(key, title) { Width = key is "Current" or "Saved" ? "36%" : "14%" });
        var status = new AntdUI.Label { Dock = DockStyle.Top, Height = 75, TextMultiLine = true, Padding = new Padding(12), Text = "正在读取备份与当前设置…" };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12) };
        var game = new AntdUI.Checkbox { Text = "游戏设置", Width = 125, Height = 36 }; var input = new AntdUI.Checkbox { Text = "按键设置", Width = 125, Height = 36 };
        var restore = new AntdUI.Button { Text = "恢复所选类别并核验", AutoSize = true, Enabled = false }; bar.Controls.AddRange([game, input, restore]);
        bool loaded = false, restoring = false;
        void UpdateRestore() => restore.Enabled = loaded && !restoring && (game.Checked || input.Checked);
        game.CheckedChanged += (_, _) => UpdateRestore(); input.CheckedChanged += (_, _) => UpdateRestore();
        Controls.Add(table); Controls.Add(bar); Controls.Add(status); AntdWindowChrome.AddHeader(this);
        Shown += async (_, _) =>
        {
            try { var preview = await features.PreviewBackupAsync(name, _requests.Token); if (IsDisposed) return; table.DataSource = preview.Differences.ToArray(); game.Enabled = game.Checked = preview.HasGameSettings; input.Enabled = input.Checked = preview.HasInputSettings; status.Text = preview.Message; loaded = true; UpdateRestore(); }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!IsDisposed) status.Text = e.Message; }
        };
        restore.Click += async (_, _) =>
        {
            if (restoring || !loaded || (!game.Checked && !input.Checked)) return;
            restoring = true; UpdateRestore();
            try { var result = await features.RestoreBackupSelectionAsync(name, game.Checked, input.Checked, _requests.Token); if (!IsDisposed) status.Text = result.Message; }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!IsDisposed) status.Text = e.Message; }
            finally { restoring = false; if (!IsDisposed) UpdateRestore(); }
        };
        Disposed += (_, _) => _requests.Dispose(); UiTheme.Apply(this);
    }
}
