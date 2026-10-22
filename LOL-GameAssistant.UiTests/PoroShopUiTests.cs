using System.Reflection;
using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.BaseViewForm;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class PoroShopUiTests
{
    [Theory]
    [InlineData(840)]
    [InlineData(1040)]
    public void SelectionShowsCombinedCostAndSubmitsOnlyCheckedItems(int width) => MatchListScrollingTests.OnUiThread(() =>
    {
        PoroShopSelection[]? submitted = null;
        var snapshot = new PoroShopSnapshot("account", 100, 30,
            [new("1", "皮肤碎片 · 雪装 奈德丽", "皮肤碎片", 10, PoroShopCurrency.Silver, 2, true, "可兑换"),
             new("2", "英雄碎片 · 刀锋之影 泰隆", "英雄碎片", 20, PoroShopCurrency.Silver, 2, true, "可兑换"),
             new("3", "限购物品", "", 10, PoroShopCurrency.Gold, 0, false, "已达兑换上限")], "");
        var features = DispatchProxy.Create<IClientFeatureService, ChampSelectCompanionTests.ServiceProxy>();
        ((ChampSelectCompanionTests.ServiceProxy)(object)features).Handler = (method, args) =>
        {
            if (method.Name == "GetPoroShopAsync") return Task.FromResult(snapshot);
            if (method.Name == "ExchangePoroShopItemsAsync")
            {
                submitted = ((IReadOnlyList<PoroShopSelection>)args[1]!).ToArray();
                return Task.FromResult(new PoroShopBatchResult(submitted.Select(s => new PoroShopItemResult(s.ItemId, "已选物品", s.Quantity,
                    s.Quantity, PoroShopExchangeState.Succeeded, "已核对兑换成功")).ToArray(), "兑换完成"));
            }
            throw new Exception("Unexpected request");
        };
        using var form = new PoroShopForm(features) { Width = width };
        form.Show(); System.Windows.Forms.Application.DoEvents();
        var controls = All(form).ToArray();
        var boxes = controls.OfType<AntdUI.Checkbox>().ToArray();
        Assert.Equal(3, boxes.Length); Assert.All(boxes, b => Assert.False(b.Checked)); Assert.False(boxes[2].Enabled);
        var exchange = controls.OfType<AntdUI.Button>().Single(b => b.Text == "兑换所选物品");
        Assert.False(exchange.Enabled);
        boxes[0].Checked = true; boxes[1].Checked = true;
        var quantities = controls.OfType<AntdUI.InputNumber>().ToArray(); quantities[0].Value = 2;
        Assert.Contains(controls.OfType<AntdUI.Label>(), label => label.Text!.Contains("合计 40 赏银"));
        Assert.True(exchange.Enabled);
        var list = controls.OfType<FlowLayoutPanel>().Single(p => p.AutoScroll);
        Assert.All(list.Controls.Cast<Control>(), row => Assert.True(row.Right <= list.ClientSize.Width, "商品行不应溢出窗口"));
        string? preview = Environment.GetEnvironmentVariable("PORO_SHOP_PREVIEW");
        if (preview != null && width == 840)
        {
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(preview);
        }
        exchange.GetType().GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(exchange, [EventArgs.Empty]);
        System.Windows.Forms.Application.DoEvents();
        Assert.NotNull(submitted);
        Assert.Equal([new PoroShopSelection("1", 2), new PoroShopSelection("2", 1)], submitted);
        Assert.Contains("兑换完成", controls.OfType<AntdUI.Input>().Single(t => t.Multiline).Text);
        Assert.All(All(form).OfType<AntdUI.Checkbox>(), b => Assert.False(b.Checked));
        form.Close();
    });

    private static IEnumerable<Control> All(Control root)
    {
        foreach (Control child in root.Controls)
        { yield return child; foreach (var nested in All(child)) yield return nested; }
    }
}
