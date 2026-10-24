namespace LOL_GameAssistant.Application.ClientFeatures;

internal static class PoroShopSelectionPolicy
{
    internal static string Category(PoroShopItem item)
    {
        string text = item.Name + " " + item.Description;
        if (text.Contains("皮肤碎片", StringComparison.Ordinal)) return "皮肤碎片";
        if (text.Contains("英雄碎片", StringComparison.Ordinal)) return "英雄碎片";
        if (text.Contains("图标", StringComparison.Ordinal) || text.Contains("头像", StringComparison.Ordinal)) return "图标";
        if (text.Contains("表情", StringComparison.Ordinal)) return "表情";
        if (text.Contains("守卫", StringComparison.Ordinal)) return "守卫皮肤";
        if (text.Contains("炫彩", StringComparison.Ordinal)) return "炫彩";
        return "其他";
    }

    // 按勾选商品的展示顺序分配余额，两种货币分别计算，避免全量兑换超支。
    internal static IReadOnlyList<PoroShopSelection> AllQuantities(PoroShopSnapshot snapshot, IEnumerable<PoroShopItem> selected)
    {
        long silver = snapshot.Silver, gold = snapshot.Gold;
        var result = new List<PoroShopSelection>();
        foreach (var item in selected)
        {
            if (!item.CanExchange || item.Remaining <= 0 || item.UnitPrice < 0) continue;
            long balance = item.Currency == PoroShopCurrency.Silver ? silver : gold;
            int quantity = (int)Math.Min(item.Remaining, item.UnitPrice == 0 ? item.Remaining : balance / item.UnitPrice);
            if (quantity <= 0) continue;
            result.Add(new(item.Id, quantity));
            long cost = item.UnitPrice * quantity;
            if (item.Currency == PoroShopCurrency.Silver) silver -= cost;
            else gold -= cost;
        }
        return result;
    }
}
