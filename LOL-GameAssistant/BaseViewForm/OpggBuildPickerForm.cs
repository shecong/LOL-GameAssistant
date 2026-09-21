using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.GameData;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>
/// OP.GG 出装路线选择框。展示多条核心装顺序和样本数据，只有用户确认后才会写入 LCU。
/// </summary>
internal sealed class OpggBuildPickerForm : Form
{
    private readonly OpggBuildChoices _choices;
    private readonly IGameAssetService _gameAssetService;
    private readonly ListView _routes = new();
    private readonly Button _apply = new() { Text = "应用选中方案", AutoSize = true, Enabled = false };

    public OpggBuildOption? SelectedOption =>
        _routes.SelectedIndices.Count == 1 && _routes.SelectedIndices[0] < _choices.Options.Count
            ? _choices.Options[_routes.SelectedIndices[0]]
            : null;

    public OpggBuildPickerForm(OpggBuildChoices choices, IGameAssetService gameAssetService)
    {
        _choices = choices;
        _gameAssetService = gameAssetService;
        Text = $"OP.GG 出装选择 · {choices.ChampionName} {choices.PositionName}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(830, 405);
        Padding = new Padding(14);

        BuildUi();
        Shown += async (_, _) => await ResolveItemNamesAsync();
    }

    private void BuildUi()
    {
        var title = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
            Text = $"{_choices.ChampionName} · {_choices.PositionName} · 选择一条核心出装路线"
        };
        var note = new Label
        {
            Dock = DockStyle.Top,
            Height = 38,
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = SystemColors.GrayText,
            Text = "胜率和样本来自 OP.GG。确认后会使用该路线写入自定义物品集，并应用 OP.GG 主流符文。"
        };
        _routes.Dock = DockStyle.Fill;
        _routes.View = View.Details;
        _routes.FullRowSelect = true;
        _routes.GridLines = true;
        _routes.HideSelection = false;
        _routes.MultiSelect = false;
        _routes.Font = new Font("Microsoft YaHei UI", 9F);
        _routes.Columns.Add("方案", 76, HorizontalAlignment.Center);
        _routes.Columns.Add("核心出装顺序", 450);
        _routes.Columns.Add("胜率", 92, HorizontalAlignment.Center);
        _routes.Columns.Add("样本", 110, HorizontalAlignment.Center);
        for (int index = 0; index < _choices.Options.Count; index++)
        {
            OpggBuildOption option = _choices.Options[index];
            var row = new ListViewItem($"方案 {option.Order}");
            row.SubItems.Add(string.Join("  →  ", option.CoreItemIds.Select(id => $"装备 {id}")));
            row.SubItems.Add($"{option.WinRate:F1}%");
            row.SubItems.Add($"{option.Matches:N0} 场");
            _routes.Items.Add(row);
        }
        if (_routes.Items.Count > 0) _routes.Items[0].Selected = true;
        _routes.SelectedIndexChanged += (_, _) => _apply.Enabled = SelectedOption != null;
        _routes.DoubleClick += (_, _) => ConfirmSelection();
        _apply.Enabled = SelectedOption != null;

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0)
        };
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        _apply.Click += (_, _) => ConfirmSelection();
        footer.Controls.Add(cancel);
        footer.Controls.Add(_apply);

        Controls.Add(_routes);
        Controls.Add(footer);
        Controls.Add(note);
        Controls.Add(title);
        AcceptButton = _apply;
        CancelButton = cancel;
    }

    private void ConfirmSelection()
    {
        if (SelectedOption == null) return;
        DialogResult = DialogResult.OK;
        Close();
    }

    private async Task ResolveItemNamesAsync()
    {
        try
        {
            var tasks = _choices.Options.Select(async option =>
            {
                var names = await Task.WhenAll(option.CoreItemIds.Select(async id =>
                    await _gameAssetService.GetItemNameAsync(id) ?? $"装备 {id}"));
                return string.Join("  →  ", names);
            }).ToArray();
            string[] routeNames = await Task.WhenAll(tasks);
            if (IsDisposed) return;
            for (int index = 0; index < routeNames.Length && index < _routes.Items.Count; index++)
                _routes.Items[index].SubItems[1].Text = routeNames[index];
        }
        catch
        {
            // 名称资源加载失败时保留装备 ID，不影响用户选方案或应用配置。
        }
    }
}
