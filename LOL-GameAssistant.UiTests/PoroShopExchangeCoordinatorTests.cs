using System.Reflection;
using LOL_GameAssistant.Application.ClientFeatures;
using Xunit;

namespace LOL_GameAssistant.UiTests;

public sealed class PoroShopExchangeCoordinatorTests
{
    [Fact]
    public void AllQuantitiesRespectSharedBalancesLimitsAndUnavailableItems()
    {
        var items = new PoroShopItem[]
        {
            new("skin", "皮肤碎片", "", 10, PoroShopCurrency.Silver, 3, true, ""),
            new("champ", "英雄碎片", "", 20, PoroShopCurrency.Silver, 5, true, ""),
            new("icon", "图标", "", 5, PoroShopCurrency.Gold, 9, true, ""),
            new("locked", "已达上限", "", 1, PoroShopCurrency.Gold, 0, false, "")
        };
        var selections = PoroShopSelectionPolicy.AllQuantities(new("account", 75, 12, items, ""), items);
        Assert.Equal([new("skin", 3), new("champ", 2), new PoroShopSelection("icon", 2)], selections);
        Assert.Equal([new PoroShopSelection("champ", 3), new("skin", 1)],
            PoroShopSelectionPolicy.AllQuantities(new("account", 75, 12, items, ""), [items[1], items[0]]));
    }

    [Fact]
    public async Task AllQuantitiesBeyondOneBatchAreSplitAndReportedAsOneJob()
    {
        var calls = new List<PoroShopSelection[]>();
        var features = Service((_, args) =>
        {
            var selections = ((IReadOnlyList<PoroShopSelection>)args[1]!).ToArray();
            calls.Add(selections);
            Assert.InRange(selections.Sum(s => s.Quantity), 1, 100);
            Assert.All(selections, s => Assert.InRange(s.Quantity, 1, 100));
            return Task.FromResult(new PoroShopBatchResult(selections.Select(s =>
                new PoroShopItemResult(s.ItemId, s.ItemId, s.Quantity, s.Quantity, PoroShopExchangeState.Succeeded, "成功")).ToArray(), "成功"));
        });
        var coordinator = PoroShopExchangeCoordinator.For(features);
        var notifications = new List<PoroShopExchangeStatus>();
        coordinator.Changed += notifications.Add;
        Assert.True(coordinator.TryStart(new("account", 10000, 0, [], ""), [new("skin", 150), new("hero", 2)]));
        await coordinator.Completion;
        Assert.Equal(2, calls.Count);
        Assert.Equal([new PoroShopSelection("skin", 100)], calls[0]);
        Assert.Equal([new PoroShopSelection("skin", 50), new("hero", 2)], calls[1]);
        var result = coordinator.Current!.Result!;
        Assert.Equal(152, result.Items.Sum(i => i.Succeeded));
        Assert.All(result.Items, i => Assert.Equal(i.Requested, i.Succeeded));
        Assert.Contains("兑换成功", result.Message);
        Assert.Single(notifications, s => s.IsRunning);
        Assert.Single(notifications, s => !s.IsRunning);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrUnconfirmedBatchStopsFollowingBatchesAndKeepsPartialResults(bool throws)
    {
        int calls = 0;
        var features = Service((_, args) =>
        {
            calls++;
            if (calls == 2 && throws) throw new InvalidOperationException("Unknown result");
            var selection = Assert.Single((IReadOnlyList<PoroShopSelection>)args[1]!);
            return Task.FromResult(new PoroShopBatchResult([new(selection.ItemId, "皮肤碎片", selection.Quantity,
                calls == 1 ? selection.Quantity : 0, calls == 1 ? PoroShopExchangeState.Succeeded : PoroShopExchangeState.Rejected,
                calls == 1 ? "成功" : "余额已变化")], "兑换结果"));
        });
        var coordinator = new PoroShopExchangeCoordinator(features);
        Assert.True(coordinator.TryStart(new("account", 10000, 0, [], ""), [new("skin", 250), new("hero", 1)]));
        await coordinator.Completion;
        Assert.Equal(2, calls);
        var result = coordinator.Current!.Result!;
        Assert.Equal(100, result.Items[0].Succeeded);
        Assert.Equal(250, result.Items[0].Requested);
        Assert.Equal(throws ? PoroShopExchangeState.Unconfirmed : PoroShopExchangeState.Rejected, result.Items[0].State);
        Assert.Equal(PoroShopExchangeState.Skipped, result.Items[1].State);
        Assert.Contains("未全部完成", result.Message);
        Assert.False(coordinator.Current.IsRunning);
    }

    private static IClientFeatureService Service(Func<MethodInfo, object?[], object?> handler)
    {
        var features = DispatchProxy.Create<IClientFeatureService, ChampSelectCompanionTests.ServiceProxy>();
        ((ChampSelectCompanionTests.ServiceProxy)(object)features).Handler = handler;
        return features;
    }
}
