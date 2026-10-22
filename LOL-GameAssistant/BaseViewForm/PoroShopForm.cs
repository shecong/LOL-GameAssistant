using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

internal sealed class PoroShopForm : AntdUI.Window, IThemeAware
{
    private readonly IClientFeatureService _features;
    private readonly UiRequestScope _requests = new();
    private readonly FlowLayoutPanel _items = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
        WrapContents = false, Padding = new Padding(16) };
    private readonly AntdUI.Label _balance = new() { Dock = DockStyle.Top, Height = 38, Padding = new Padding(16, 0, 16, 0) };
    private readonly AntdUI.Label _total = new() { Dock = DockStyle.Top, Height = 38, Padding = new Padding(16, 0, 16, 0) };
    private readonly AntdUI.Input _results = new() { Dock = DockStyle.Bottom, Height = 110, Multiline = true, ReadOnly = true, AutoScroll = true };
    private readonly AntdUI.Button _exchange = new() { Text = "兑换所选物品", AutoSize = true, Enabled = false };
    private readonly List<(PoroShopItem Item, AntdUI.Checkbox Check, AntdUI.InputNumber Quantity)> _rows = [];
    private PoroShopSnapshot? _preview;
    private bool _busy;

    internal PoroShopForm(IClientFeatureService features)
    {
        _features = features;
        Text = "魄罗商店 · 多选兑换"; Size = new Size(880, 720); MinimumSize = new Size(840, 560); StartPosition = FormStartPosition.CenterParent;
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(12, 4, 12, 4) };
        var refresh = new AntdUI.Button { Text = "刷新物品与余额", AutoSize = true };
        var all = new AntdUI.Button { Text = "全选可兑换", AutoSize = true };
        var clear = new AntdUI.Button { Text = "清空勾选", AutoSize = true };
        refresh.Click += async (_, _) => await RefreshShopAsync();
        all.Click += (_, _) => { if (!_busy) foreach (var row in _rows) row.Check.Checked = row.Item.CanExchange; };
        clear.Click += (_, _) => { if (!_busy) foreach (var row in _rows) row.Check.Checked = false; };
        _exchange.Click += async (_, _) => await ExchangeAsync();
        actions.Controls.AddRange([refresh, all, clear, _exchange]);
        Controls.Add(_items); Controls.Add(_results); Controls.Add(_total); Controls.Add(_balance); Controls.Add(actions);
        AntdWindowChrome.AddHeader(this); UiTheme.Apply(this);
        Shown += async (_, _) => await RefreshShopAsync(); Disposed += (_, _) => _requests.Dispose();
    }

    private async Task RefreshShopAsync()
    {
        if (_busy || IsDisposed) return;
        SetBusy(true); _balance.Text = "正在读取魄罗商店…";
        try
        {
            var preview = await _features.GetPoroShopAsync(_requests.Token);
            if (IsDisposed) return;
            _preview = preview; _rows.Clear(); ControlLifetime.ClearAndDispose(_items);
            foreach (var item in preview.Items)
            {
                var row = new FlowLayoutPanel { Width = 760, Height = 88, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                    Padding = new Padding(6), Margin = new Padding(0, 0, 0, 8) };
                var check = new AntdUI.Checkbox { Text = item.Name, Width = 720, Height = 30, Enabled = item.CanExchange };
                var detail = new FlowLayoutPanel { Width = 720, Height = 40, WrapContents = false };
                detail.Controls.Add(new AntdUI.Label { Text = $"{item.UnitPrice} {Currency(item.Currency)} / 份 · 剩余 {item.Remaining} 次 · {item.Availability}", Width = 570, Height = 30 });
                var quantity = new AntdUI.InputNumber { Minimum = 1, Maximum = Math.Max(1, Math.Min(100, item.Remaining)), Value = 1,
                    DecimalPlaces = 0, Increment = 1, Width = 110, Height = 36, Enabled = item.CanExchange };
                detail.Controls.Add(quantity); row.Controls.Add(check); row.Controls.Add(detail); _items.Controls.Add(row);
                _rows.Add((item, check, quantity));
                check.CheckedChanged += (_, _) => UpdateTotals(); quantity.ValueChanged += (_, _) => UpdateTotals();
            }
            _balance.Text = $"余额：{preview.Silver} 魄罗赏银 · {preview.Gold} 魄罗赏金";
            if (preview.Items.Count == 0) _balance.Text += " · " + preview.Message;
            UiTheme.Apply(_items);
            AntdContentTheme.Apply(_items, UiTheme.Palette);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!IsDisposed) { _preview = null; _balance.Text = "商店读取失败，请确认客户端已登录后刷新"; } }
        finally { if (!IsDisposed) SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        foreach (var row in _rows) { row.Check.Enabled = !busy && row.Item.CanExchange; row.Quantity.Enabled = !busy && row.Item.CanExchange; }
        UpdateTotals();
    }

    private void UpdateTotals()
    {
        var selected = _rows.Where(r => r.Check.Checked).ToArray();
        decimal silver = selected.Where(r => r.Item.Currency == PoroShopCurrency.Silver).Sum(r => (decimal)r.Item.UnitPrice * (int)r.Quantity.Value);
        decimal gold = selected.Where(r => r.Item.Currency == PoroShopCurrency.Gold).Sum(r => (decimal)r.Item.UnitPrice * (int)r.Quantity.Value);
        int count = selected.Sum(r => (int)r.Quantity.Value);
        bool affordable = _preview != null && silver <= _preview.Silver && gold <= _preview.Gold;
        _total.Text = $"已选 {selected.Length} 种 / {count} 份 · 合计 {silver} 赏银、{gold} 赏金" +
            (count > 100 ? " · 单批最多 100 份" : !affordable && count > 0 ? " · 总价超过余额" : "");
        _exchange.Enabled = !_busy && affordable && count is > 0 and <= 100;
    }

    private async Task ExchangeAsync()
    {
        if (_busy || _preview == null || !_exchange.Enabled) return;
        var selected = _rows.Where(r => r.Check.Checked).Select(r => new PoroShopSelection(r.Item.Id, (int)r.Quantity.Value)).ToArray();
        SetBusy(true); string resultText = "";
        try
        {
            var result = await _features.ExchangePoroShopItemsAsync(_preview, selected, _requests.Token);
            resultText = result.Message + Environment.NewLine + string.Join(Environment.NewLine,
                result.Items.Select(r => $"{r.Name}：{r.Succeeded}/{r.Requested} 份 · {r.Message}"));
        }
        catch (OperationCanceledException) { resultText = "已取消，请刷新并核对已提交的兑换结果"; }
        catch (Exception) { resultText = "兑换结果未确认，请在客户端核对后刷新；未自动重试"; }
        finally { if (!IsDisposed) SetBusy(false); }
        if (!IsDisposed) { _results.Text = resultText; await RefreshShopAsync(); }
    }

    public void ApplyTheme(ThemePalette palette) => AntdContentTheme.Apply(this, palette);

    private static string Currency(PoroShopCurrency currency) => currency == PoroShopCurrency.Silver ? "魄罗赏银" : "魄罗赏金";
}
