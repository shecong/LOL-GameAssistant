using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

internal sealed class PoroShopForm : AntdUI.Window, IThemeAware
{
    private readonly IClientFeatureService _features;
    private readonly PoroShopExchangeCoordinator _exchanges;
    private readonly UiRequestScope _requests = new();
    private readonly FlowLayoutPanel _items = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
        WrapContents = false, Padding = new Padding(16) };
    private readonly AntdUI.Label _balance = new() { Dock = DockStyle.Top, Height = 38, Padding = new Padding(16, 0, 16, 0) };
    private readonly AntdUI.Label _total = new() { Dock = DockStyle.Top, Height = 38, Padding = new Padding(16, 0, 16, 0) };
    private readonly AntdUI.Input _results = new() { Dock = DockStyle.Bottom, Height = 110, Multiline = true, ReadOnly = true, AutoScroll = true };
    private readonly AntdUI.Button _exchange = new() { Text = "兑换所选物品", AutoSize = true, Enabled = false };
    private readonly AntdUI.Checkbox _allQuantities = new() { Name = "exchangeAllQuantities", Text = "兑换全部数量", Checked = true, AutoSize = true };
    private readonly FlowLayoutPanel _categories = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(16, 4, 16, 4) };
    private readonly Dictionary<string, AntdUI.Checkbox> _categoryChecks = [];
    private readonly List<(PoroShopItem Item, AntdUI.Checkbox Check, AntdUI.InputNumber Quantity)> _rows = [];
    private PoroShopSnapshot? _preview;
    private bool _busy;
    private bool _updatingSelection;
    private bool _needsRefresh;

    internal PoroShopForm(IClientFeatureService features)
    {
        _features = features; _exchanges = PoroShopExchangeCoordinator.For(features);
        Text = "魄罗商店 · 多选兑换"; Size = new Size(880, 760); MinimumSize = new Size(840, 600); StartPosition = FormStartPosition.CenterParent;
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(12, 4, 12, 4) };
        var refresh = new AntdUI.Button { Text = "刷新物品与余额", AutoSize = true };
        var all = new AntdUI.Button { Text = "全选可兑换", AutoSize = true };
        var clear = new AntdUI.Button { Text = "清空勾选", AutoSize = true };
        refresh.Click += async (_, _) => await RefreshShopAsync();
        all.Click += (_, _) => SelectItems(_ => true, true);
        clear.Click += (_, _) => SelectItems(_ => true, false);
        _allQuantities.CheckedChanged += (_, _) => UpdateTotals();
        _exchange.Click += (_, _) => StartExchange();
        actions.Controls.AddRange([refresh, all, clear, _exchange, _allQuantities]);
        var note = new AntdUI.Label { Dock = DockStyle.Top, Height = 38, Padding = new Padding(16, 0, 16, 0),
            Text = "全部数量按限购及余额计算；取消勾选可自填数量。提交后可关闭窗口，完成时会通知。" };
        Controls.Add(_items); Controls.Add(_results); Controls.Add(_total); Controls.Add(_balance);
        Controls.Add(_categories); Controls.Add(note); Controls.Add(actions);
        AntdWindowChrome.AddHeader(this); UiTheme.Apply(this);
        _items.ClientSizeChanged += (_, _) => ResizeRows();
        _exchanges.Changed += ExchangeChanged;
        Shown += async (_, _) => await RefreshShopAsync();
        Disposed += (_, _) => { _exchanges.Changed -= ExchangeChanged; _requests.Dispose(); };
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Modeless Show(owner) does not apply CenterParent automatically.
        if (Owner != null) CenterToParent();
    }

    private async Task RefreshShopAsync()
    {
        if (_busy || IsDisposed) return;
        SetBusy(true); _balance.Text = "正在读取魄罗商店…";
        try
        {
            var preview = await _features.GetPoroShopAsync(_requests.Token);
            if (IsDisposed) return;
            _preview = preview; _needsRefresh = false; _rows.Clear(); ControlLifetime.ClearAndDispose(_items);
            foreach (var item in preview.Items)
            {
                var row = new FlowLayoutPanel { Width = 760, Height = 88, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                    Padding = new Padding(6), Margin = new Padding(0, 0, 0, 8) };
                var check = new AntdUI.Checkbox { Name = "item_" + item.Id, Text = item.Name, Width = 720, Height = 30, Enabled = item.CanExchange };
                var detail = new FlowLayoutPanel { Width = 720, Height = 40, WrapContents = false };
                detail.Controls.Add(new AntdUI.Label { Text = $"{item.UnitPrice} {Currency(item.Currency)} / 份 · 剩余 {item.Remaining} 次 · {item.Availability}", Width = 570, Height = 30 });
                var quantity = new AntdUI.InputNumber { Name = "quantity_" + item.Id, Minimum = 0, Maximum = Math.Max(1, item.Remaining), Value = 1,
                    DecimalPlaces = 0, Increment = 1, Width = 110, Height = 36, Enabled = false };
                detail.Controls.Add(quantity); row.Controls.Add(check); row.Controls.Add(detail); _items.Controls.Add(row);
                _rows.Add((item, check, quantity));
                check.CheckedChanged += (_, _) => UpdateTotals(); quantity.ValueChanged += (_, _) => UpdateTotals();
            }
            BuildCategories(); ResizeRows();
            _balance.Text = $"余额：{preview.Silver} 魄罗赏银 · {preview.Gold} 魄罗赏金";
            if (preview.Items.Count == 0) _balance.Text += " · " + preview.Message;
            RenderExchangeStatus();
            UiTheme.Apply(_items); UiTheme.Apply(_categories);
            AntdContentTheme.Apply(_items, UiTheme.Palette);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!IsDisposed) { _preview = null; _balance.Text = "商店读取失败，请确认客户端已登录后刷新"; } }
        finally { if (!IsDisposed) SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UpdateTotals();
    }

    private void BuildCategories()
    {
        _categoryChecks.Clear(); ControlLifetime.ClearAndDispose(_categories);
        foreach (string category in new[] { "皮肤碎片", "英雄碎片", "图标", "表情", "守卫皮肤", "炫彩", "其他" })
        {
            if (!_rows.Any(r => PoroShopSelectionPolicy.Category(r.Item) == category)) continue;
            var check = new AntdUI.Checkbox { Name = "category_" + category, Text = "全选" + category, AutoSize = true };
            check.CheckedChanged += (_, _) => { if (!_updatingSelection) SelectItems(item => PoroShopSelectionPolicy.Category(item) == category, check.Checked); };
            _categoryChecks.Add(category, check); _categories.Controls.Add(check);
        }
    }

    private void ResizeRows()
    {
        int width = Math.Max(200, _items.ClientSize.Width - _items.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
        foreach (Control row in _items.Controls)
        {
            row.Width = width; row.Controls[0].Width = width - 20;
            var detail = row.Controls[1]; detail.Width = width - 20;
            detail.Controls[0].Width = Math.Max(60, detail.Width - 125);
        }
    }

    private void SelectItems(Func<PoroShopItem, bool> predicate, bool selected)
    {
        if (_busy || _needsRefresh || _exchanges.Current?.IsRunning == true) return;
        _updatingSelection = true;
        try { foreach (var row in _rows.Where(r => predicate(r.Item))) row.Check.Checked = selected && row.Item.CanExchange; }
        finally { _updatingSelection = false; }
        UpdateTotals();
    }

    private void UpdateTotals()
    {
        if (_updatingSelection || IsDisposed) return;
        _updatingSelection = true;
        try
        {
            bool running = _exchanges.Current?.IsRunning == true;
            bool editable = !_busy && !running && !_needsRefresh;
            _allQuantities.Enabled = editable;
            var quantities = _preview != null && _allQuantities.Checked
                ? PoroShopSelectionPolicy.AllQuantities(_preview, _rows.Where(r => r.Check.Checked).Select(r => r.Item)).ToDictionary(s => s.ItemId, s => s.Quantity)
                : null;
            foreach (var row in _rows)
            {
                row.Check.Enabled = editable && row.Item.CanExchange;
                row.Quantity.Minimum = _allQuantities.Checked ? 0 : 1;
                if (!_allQuantities.Checked && row.Quantity.Value < 1) row.Quantity.Value = 1;
                if (quantities != null)
                    row.Quantity.Value = row.Check.Checked ? quantities.GetValueOrDefault(row.Item.Id) :
                        PoroShopSelectionPolicy.AllQuantities(_preview!, [row.Item]).FirstOrDefault()?.Quantity ?? 0;
                row.Quantity.Enabled = editable && row.Item.CanExchange && !_allQuantities.Checked;
            }
            foreach (var (category, check) in _categoryChecks)
            {
                var rows = _rows.Where(r => r.Item.CanExchange && PoroShopSelectionPolicy.Category(r.Item) == category).ToArray();
                check.Enabled = editable && rows.Length > 0;
                check.Checked = rows.Length > 0 && rows.All(r => r.Check.Checked);
            }
            var selected = _rows.Where(r => r.Check.Checked).ToArray();
            decimal silver = selected.Where(r => r.Item.Currency == PoroShopCurrency.Silver).Sum(r => (decimal)r.Item.UnitPrice * (int)r.Quantity.Value);
            decimal gold = selected.Where(r => r.Item.Currency == PoroShopCurrency.Gold).Sum(r => (decimal)r.Item.UnitPrice * (int)r.Quantity.Value);
            long count = selected.Sum(r => (long)r.Quantity.Value);
            bool affordable = _preview != null && silver <= _preview.Silver && gold <= _preview.Gold;
            _total.Text = running ? "正在后台兑换，可关闭窗口或切换页面，完成后会通知" :
                $"已选 {selected.Length} 种 / {count} 份 · 合计 {silver} 赏银、{gold} 赏金" +
                (!affordable && count > 0 ? " · 总价超过余额" : selected.Any(r => r.Quantity.Value == 0) ? " · 部分商品余额不足，本次跳过" : "");
            _exchange.Enabled = editable && affordable && count > 0;
        }
        finally { _updatingSelection = false; }
    }

    private void StartExchange()
    {
        if (_busy || _preview == null || !_exchange.Enabled) return;
        var selected = _rows.Where(r => r.Check.Checked && r.Quantity.Value > 0)
            .Select(r => new PoroShopSelection(r.Item.Id, (int)r.Quantity.Value)).ToArray();
        bool started = _exchanges.TryStart(_preview, selected);
        if (started) _needsRefresh = true;
        _results.Text = started
            ? "已提交后台兑换，可关闭本窗口。成功或未全部完成时会主动提示。"
            : "已有兑换任务正在后台进行，请等待完成通知。";
        UpdateTotals();
    }

    private void ExchangeChanged(PoroShopExchangeStatus status)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(new Action(async () =>
            {
                if (IsDisposed || !ReferenceEquals(_exchanges.Current, status)) return;
                RenderExchangeStatus(); UpdateTotals();
                if (!status.IsRunning && _preview?.AccountKey == status.AccountKey) await RefreshShopAsync();
            }));
        }
        catch (InvalidOperationException) { }
    }

    private void RenderExchangeStatus()
    {
        var status = _exchanges.Current;
        if (status?.IsRunning == true) _results.Text = "兑换正在后台进行，可关闭窗口，完成后会主动提示。";
        else if (status?.AccountKey == _preview?.AccountKey && status?.Result is { } result)
            _results.Text = result.Message + Environment.NewLine + string.Join(Environment.NewLine,
                result.Items.Select(r => $"{r.Name}：{r.Succeeded}/{r.Requested} 份 · {r.Message}"));
        else _results.Text = "";
    }

    public void ApplyTheme(ThemePalette palette) => AntdContentTheme.Apply(this, palette);

    private static string Currency(PoroShopCurrency currency) => currency == PoroShopCurrency.Silver ? "魄罗赏银" : "魄罗赏金";
}
