using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

internal sealed class TclsAccountsForm : AntdUI.Window, IThemeAware
{
    private readonly ITclsAccountStore _store;
    private readonly AntdUI.Select _accounts = new() { List = true, DropDownArrow = true, Dock = DockStyle.Fill, Name = "tclsSavedAccounts", PlaceholderText = "请选择已保存账号" };
    private readonly AntdUI.Input _name = new() { Dock = DockStyle.Fill, Name = "tclsAccountName" };
    private readonly AntdUI.Input _username = new() { Dock = DockStyle.Fill, Name = "tclsUsername" };
    private readonly AntdUI.Input _password = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true, Name = "tclsPassword", PlaceholderText = "留空则保留已保存的密码" };
    private readonly AntdUI.Select _region = new() { Dock = DockStyle.Fill, DropDownArrow = true, Name = "tclsRegion", PlaceholderText = "选择或输入大区" };
    private readonly AntdUI.Label _status = new() { Dock = DockStyle.Fill, TextMultiLine = true, Name = "tclsAccountStatus" };
    private string? _editing;
    internal string? SelectedAccountId { get; private set; }

    internal TclsAccountsForm(ITclsAccountStore store, string? selectedAccountId)
    {
        _store = store;
        Text = "TCLS 登录账号";Name = "tclsAccountManager";
        ClientSize = new Size(620, 490);MinimumSize = Size;
        Font = new Font(UiMetrics.FontFamily, 10F);
        StartPosition = FormStartPosition.CenterParent;ShowInTaskbar = false;
        AntdWindowChrome.Configure(this, fitWorkingArea: false);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 2, RowCount = 8 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(int row, string label, Control control)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.Controls.Add(new AntdUI.Label { Text = label, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 12) }, 0, row);
            control.Margin = new Padding(0, 0, 0, 12);layout.Controls.Add(control, 1, row);
        }
        Row(0, "已保存账号", _accounts);Row(1, "备注名称", _name);Row(2, "登录账号", _username);
        Row(3, "登录密码", _password);Row(4, "默认大区", _region);
        _region.Items.AddRange(new object[] { "艾欧尼亚", "祖安", "诺克萨斯", "班德尔城", "皮尔特沃夫", "战争学院", "巨神峰",
            "雷瑟守备", "裁决之地", "黑色玫瑰", "暗影岛", "钢铁烈阳", "均衡教派", "水晶之痕", "影流", "守望之海",
            "征服之海", "卡拉曼达", "皮城警备", "比尔吉沃特", "德玛西亚", "弗雷尔卓德", "无畏先锋", "恕瑞玛", "扭曲丛林", "巨龙之巢", "教育网专区", "男爵领域", "峡谷之巅" });
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var note = new AntdUI.Label { Text = "密码仅在当前 Windows 用户下加密保存。修改已有账号时，密码留空表示保留原密码。大区名称应与登录器显示一致，可手动输入。", Dock = DockStyle.Fill, TextMultiLine = true };
        layout.Controls.Add(note, 0, 5);layout.SetColumnSpan(note, 2);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 14, 0, 8) };
        void Button(string text, Action action)
        {
            var button = new AntdUI.Button { Text = text, AutoSize = true, Height = 34 };
            button.Click += (_, _) => { try { action(); } catch (Exception) { _status.Text = "操作未完成，请检查填写内容、账号文件及本机加密功能。"; } };
            actions.Controls.Add(button);
        }
        Button("新增", NewAccount);Button("保存账号", SaveAccount);Button("删除所选", DeleteAccount);Button("关闭", Close);
        layout.Controls.Add(actions, 0, 6);layout.SetColumnSpan(actions, 2);
        layout.Controls.Add(_status, 0, 7);layout.SetColumnSpan(_status, 2);Controls.Add(layout);
        var header = new AntdUI.PageHeader { Text = "TCLS 登录账号", Dock = DockStyle.Top, Height = 42,
            ShowButton = true, MinimizeBox = false, UseSystemStyleColor = false, UseForeColorDrawIcons = true };
        Controls.Add(header);
        _accounts.SelectedIndexChanged += (_, _) =>
        {
            if (_accounts.SelectedValue is not TclsAccount account) return;
            _editing = account.Id;SelectedAccountId = account.Id;
            _name.Text = account.Name;_username.Text = account.Username;_region.Text = account.Region;_password.Text = "";
            _status.Text = "密码留空可保留已保存的密码。";
        };
        LoadAccounts(selectedAccountId);
        UiTheme.Apply(this);
        FormClosed += (_, _) => _password.Text = "";
    }

    public void ApplyTheme(ThemePalette palette) => AntdContentTheme.Apply(this, palette);

    private void LoadAccounts(string? selected)
    {
        _accounts.Items.Clear();foreach (var account in _store.Load()) _accounts.Items.Add(new AntdUI.SelectItem(account.ToString(), account));
        var match = _accounts.Items.Cast<AntdUI.SelectItem>().FirstOrDefault(item => (item.Tag as TclsAccount)?.Id == selected);
        if (match != null) _accounts.SelectedValue = match.Tag;
        else if (_accounts.Items.Count > 0) _accounts.SelectedIndex = 0;
        else NewAccount();
    }
    private void NewAccount()
    {
        _accounts.SelectedIndex = -1;_accounts.Text = "";_editing = null;_name.Text = "";_username.Text = "";_password.Text = "";_region.SelectedIndex = -1;_region.Text = "";
        _status.Text = "填写账号、密码和默认大区后保存。";
    }
    private void SaveAccount()
    {
        if (string.IsNullOrWhiteSpace(_username.Text) || string.IsNullOrWhiteSpace(_region.Text) || (_editing == null && _password.Text.Length == 0))
        { _status.Text = "请填写账号、默认大区；首次保存还需要密码。";return; }
        var account = _store.Save(_editing, _name.Text, _username.Text, _region.Text, _password.Text);
        _password.Text = "";SelectedAccountId = account.Id;LoadAccounts(account.Id);_status.Text = "账号已保存，密码已在本机加密。";
    }
    private void DeleteAccount()
    {
        if (_editing == null) return;
        _store.Delete(_editing);SelectedAccountId = null;LoadAccounts(null);_status.Text = "所选账号已删除。";
    }
}
