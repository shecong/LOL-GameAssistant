using System.Reflection;
using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.BaseViewForm;
using LOL_GameAssistant.Helper;
using Xunit;

namespace LOL_GameAssistant.UiTests;

[Collection("Window theme")]
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
        var boxes = controls.OfType<AntdUI.Checkbox>().Where(b => b.Name.StartsWith("item_")).ToArray();
        Assert.Equal(3, boxes.Length); Assert.All(boxes, b => Assert.False(b.Checked)); Assert.False(boxes[2].Enabled);
        var allQuantities = controls.OfType<AntdUI.Checkbox>().Single(b => b.Name == "exchangeAllQuantities");
        Assert.True(allQuantities.Checked);
        var exchange = controls.OfType<AntdUI.Button>().Single(b => b.Text == "兑换所选物品");
        Assert.False(exchange.Enabled);
        boxes[0].Checked = true; boxes[1].Checked = true;
        Assert.All(controls.OfType<AntdUI.InputNumber>().Take(2), q => { Assert.Equal(2, q.Value); Assert.False(q.Enabled); });
        Assert.Contains(controls.OfType<AntdUI.Label>(), label => label.Text!.Contains("合计 60 赏银"));
        allQuantities.Checked = false;
        var quantities = controls.OfType<AntdUI.InputNumber>().ToArray(); quantities[0].Value = 2;
        quantities[1].Value = 1;
        Assert.Contains(controls.OfType<AntdUI.Label>(), label => label.Text!.Contains("合计 40 赏银"));
        Assert.True(exchange.Enabled);
        var list = controls.OfType<FlowLayoutPanel>().Single(p => p.AutoScroll);
        Assert.All(list.Controls.Cast<Control>(), row => Assert.True(row.Right <= list.ClientSize.Width, "商品行不应溢出窗口"));
        string? preview = Environment.GetEnvironmentVariable("PORO_SHOP_PREVIEW");
        if (preview != null && width == 840)
        {
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.ChangeExtension(preview, "manual.png"));
        }
        exchange.GetType().GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(exchange, [EventArgs.Empty]);
        PumpUntil(() => PoroShopExchangeCoordinator.For(features).Completion.IsCompleted &&
            All(form).OfType<AntdUI.Input>().Single(t => t.Multiline).Text!.Contains("兑换成功"));
        Assert.NotNull(submitted);
        Assert.Equal([new PoroShopSelection("1", 2), new PoroShopSelection("2", 1)], submitted);
        Assert.Contains("兑换成功", controls.OfType<AntdUI.Input>().Single(t => t.Multiline).Text);
        Assert.All(All(form).OfType<AntdUI.Checkbox>().Where(b => b.Name.StartsWith("item_")), b => Assert.False(b.Checked));
        form.Close();
    });

    [Fact]
    public void CategoriesSelectOnlyTheirAvailableItemsAndRemainInSync() => MatchListScrollingTests.OnUiThread(() =>
    {
        var items = new PoroShopItem[]
        {
            new("skin1", "皮肤碎片 · 雪装 奈德丽", "", 10, PoroShopCurrency.Silver, 3, true, "可兑换"),
            new("skin2", "皮肤碎片 · 提莫", "", 10, PoroShopCurrency.Silver, 2, true, "可兑换"),
            new("locked", "皮肤碎片 · 已兑换", "", 10, PoroShopCurrency.Silver, 0, false, "已达兑换上限"),
            new("champ", "英雄碎片 · 泰隆", "", 20, PoroShopCurrency.Silver, 2, true, "可兑换"),
            new("icon", "魄罗头像图标", "", 10, PoroShopCurrency.Gold, 1, true, "可兑换"),
            new("emote", "魄罗表情", "", 10, PoroShopCurrency.Silver, 1, true, "可兑换"),
            new("ward", "守卫皮肤", "", 10, PoroShopCurrency.Silver, 1, true, "可兑换"),
            new("chroma", "炫彩", "", 10, PoroShopCurrency.Silver, 1, true, "可兑换"),
            new("other", "宝箱", "", 10, PoroShopCurrency.Silver, 1, true, "可兑换")
        };
        var features = Service((method, _) => method.Name == "GetPoroShopAsync"
            ? Task.FromResult(new PoroShopSnapshot("account", 100, 30, items, "")) : throw new Exception("No exchange expected"));
        using var form = new PoroShopForm(features) { Width = 840 };
        form.Show(); System.Windows.Forms.Application.DoEvents();
        AntdUI.Checkbox Box(string name) => All(form).OfType<AntdUI.Checkbox>().Single(b => b.Name == name);
        Box("category_皮肤碎片").Checked = true;
        Assert.True(Box("item_skin1").Checked); Assert.True(Box("item_skin2").Checked);
        Assert.False(Box("item_locked").Checked); Assert.False(Box("item_champ").Checked); Assert.False(Box("item_icon").Checked);
        Box("category_英雄碎片").Checked = true; Box("category_图标").Checked = true;
        Assert.True(Box("item_champ").Checked); Assert.True(Box("item_icon").Checked);
        Box("item_skin1").Checked = false; Assert.False(Box("category_皮肤碎片").Checked);
        Box("category_皮肤碎片").Checked = true;
        Box("category_皮肤碎片").Checked = false;
        Assert.False(Box("item_skin1").Checked); Assert.False(Box("item_skin2").Checked);
        Assert.True(Box("item_champ").Checked); Assert.True(Box("item_icon").Checked);
        foreach (var panel in All(form).OfType<FlowLayoutPanel>().Where(p => p.Controls.Cast<Control>().Any(c => c.Name.StartsWith("category_"))))
            Assert.All(panel.Controls.Cast<Control>(), check => Assert.True(check.Bottom <= panel.ClientSize.Height, "类型选择不应被截断"));
        string? preview = Environment.GetEnvironmentVariable("PORO_SHOP_PREVIEW");
        if (preview != null)
        {
            string originalMode = UiTheme.Mode;
            try
            {
                UiTheme.SetMode("Dark"); UiTheme.Apply(form);
                using var bitmap = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(preview);
            }
            finally { UiTheme.SetMode(originalMode); }
        }
        form.Close();
    });

    [Fact]
    public void ClosingAndReopeningTheShopDoesNotCancelOrRepeatBackgroundExchange() => MatchListScrollingTests.OnUiThread(() =>
    {
        var pending = new TaskCompletionSource<PoroShopBatchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken submittedToken = default;
        int submissions = 0, notifications = 0;
        var snapshot = new PoroShopSnapshot("account", 100, 0,
            [new("skin", "皮肤碎片", "", 10, PoroShopCurrency.Silver, 3, true, "可兑换")], "");
        var features = Service((method, args) =>
        {
            if (method.Name == "GetPoroShopAsync") return Task.FromResult(snapshot);
            submissions++; submittedToken = (CancellationToken)args[2]!;
            Assert.Equal(new PoroShopSelection("skin", 3), Assert.Single((IReadOnlyList<PoroShopSelection>)args[1]!));
            entered.TrySetResult(); return pending.Task;
        });
        var exchanges = PoroShopExchangeCoordinator.For(features);
        exchanges.Changed += status => { if (!status.IsRunning) Interlocked.Increment(ref notifications); };
        using (var form = new PoroShopForm(features))
        {
            form.Show(); System.Windows.Forms.Application.DoEvents();
            All(form).OfType<AntdUI.Checkbox>().Single(b => b.Name == "category_皮肤碎片").Checked = true;
            Click(All(form).OfType<AntdUI.Button>().Single(b => b.Text == "兑换所选物品"));
            PumpUntil(() => entered.Task.IsCompleted);
            Assert.True(exchanges.Current!.IsRunning); Assert.False(exchanges.Completion.IsCompleted);
            form.Close();
        }
        Assert.False(submittedToken.IsCancellationRequested);
        using var reopened = new PoroShopForm(features);
        reopened.Show(); System.Windows.Forms.Application.DoEvents();
        Assert.False(All(reopened).OfType<AntdUI.Button>().Single(b => b.Text == "兑换所选物品").Enabled);
        Assert.Contains("后台", All(reopened).OfType<AntdUI.Input>().Single(t => t.Multiline).Text);
        Assert.False(exchanges.TryStart(snapshot, [new("skin", 3)]));
        pending.SetResult(new([new("skin", "皮肤碎片", 3, 3, PoroShopExchangeState.Succeeded, "已核对兑换成功")], "兑换完成"));
        PumpUntil(() => exchanges.Completion.IsCompleted && All(reopened).OfType<AntdUI.Input>().Single(t => t.Multiline).Text!.Contains("兑换成功"));
        Assert.Equal(1, submissions); Assert.Equal(1, notifications);
        Assert.False(exchanges.Current!.IsRunning);
        reopened.Close();
    });

    private static IClientFeatureService Service(Func<MethodInfo, object?[], object?> handler)
    {
        var features = DispatchProxy.Create<IClientFeatureService, ChampSelectCompanionTests.ServiceProxy>();
        ((ChampSelectCompanionTests.ServiceProxy)(object)features).Handler = handler;
        return features;
    }

    private static void Click(AntdUI.Button button) =>
        button.GetType().GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(button, [EventArgs.Empty]);

    private static void PumpUntil(Func<bool> condition)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(5))
        { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(1); }
        Assert.True(condition());
    }

    private static IEnumerable<Control> All(Control root)
    {
        foreach (Control child in root.Controls)
        { yield return child; foreach (var nested in All(child)) yield return nested; }
    }
}
