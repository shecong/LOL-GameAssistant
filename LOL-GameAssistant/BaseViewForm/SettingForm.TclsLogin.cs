using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.LeagueClient;

namespace LOL_GameAssistant.BaseViewForm;

public partial class SettingForm
{
    private readonly LOL_GameAssistant.Application.LeagueClient.ITclsAccountStore _tclsAccountStore;
    private readonly AntdUI.Checkbox _tclsAutoLogin = new() { Text = "启动客户端时自动填写 TCLS 账号密码并选择大区", AutoSize = true, Name = "tclsAutoLogin" };
    private readonly AntdUI.Select _tclsAccounts = new() { List = true, DropDownArrow = true, Dock = DockStyle.Fill, Height = 40, Name = "tclsSelectedAccount", PlaceholderText = "请选择 TCLS 账号" };
    private bool _refreshingTclsAccounts;

    private Control CreateTclsAccountRow()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var manage = new AntdUI.Button { Text = "管理账号", AutoSize = true, Height = 34, Name = "manageTclsAccounts" };
        var cancel = new AntdUI.Button { Text = "取消登录", AutoSize = true, Height = 34, Name = "cancelTclsLogin" };
        manage.Click += (_, _) =>
        {
            AppCompositionRoot.TclsLoginLauncher.Cancel();
            using var dialog = new TclsAccountsForm(_tclsAccountStore, _config.TclsSelectedAccountId);
            dialog.ShowDialog(FindForm());
            RefreshTclsAccounts(dialog.SelectedAccountId);
            string selected = (_tclsAccounts.SelectedValue as TclsAccount)?.Id ?? "";
            PersistValue(settings => { settings.TclsSelectedAccountId = selected;if (selected.Length == 0) settings.TclsAutoLoginEnabled = false; });
            _tclsAutoLogin.Checked = _config.TclsAutoLoginEnabled;
        };
        cancel.Click += (_, _) => { AppCompositionRoot.TclsLoginLauncher.Cancel();_clientStatus.Text = "已请求取消 TCLS 自动登录。"; };
        _tclsAccounts.SelectedIndexChanged += (_, _) =>
        {
            if (!_refreshingTclsAccounts && _tclsAccounts.SelectedValue is TclsAccount account)
            {
                AppCompositionRoot.TclsLoginLauncher.Cancel();
                PersistValue(settings => settings.TclsSelectedAccountId = account.Id);
            }
        };
        row.Controls.Add(_tclsAccounts, 0, 0);row.Controls.Add(manage, 1, 0);row.Controls.Add(cancel, 2, 0);
        RefreshTclsAccounts(_config.TclsSelectedAccountId);
        return row;
    }

    private void RefreshTclsAccounts(string? selected)
    {
        _refreshingTclsAccounts = true;
        try
        {
            _tclsAccounts.Items.Clear();
            _tclsAccounts.SelectedIndex = -1;_tclsAccounts.Text = "";
            foreach (var account in _tclsAccountStore.Load()) _tclsAccounts.Items.Add(new AntdUI.SelectItem(account.ToString(), account));
            var match = _tclsAccounts.Items.Cast<AntdUI.SelectItem>().FirstOrDefault(item => (item.Tag as TclsAccount)?.Id == selected);
            if (match != null) _tclsAccounts.SelectedValue = match.Tag;
            else if (_tclsAccounts.Items.Count > 0) _tclsAccounts.SelectedIndex = 0;
            _tclsAutoLogin.Enabled = _tclsAccounts.Items.Count > 0;
        }
        finally { _refreshingTclsAccounts = false; }
    }
}
