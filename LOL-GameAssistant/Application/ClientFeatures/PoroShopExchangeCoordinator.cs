using System.Runtime.CompilerServices;

namespace LOL_GameAssistant.Application.ClientFeatures;

internal sealed record PoroShopExchangeStatus(string AccountKey, bool IsRunning, PoroShopBatchResult? Result);

/// <summary>兑换任务由应用持有，关闭商店窗口不会取消；同一服务只允许一个兑换任务。</summary>
internal sealed class PoroShopExchangeCoordinator(IClientFeatureService features)
{
    private static readonly ConditionalWeakTable<IClientFeatureService, PoroShopExchangeCoordinator> Instances = new();
    internal static PoroShopExchangeCoordinator For(IClientFeatureService features) => Instances.GetValue(features, service => new(service));
    private readonly object _gate = new();
    private PoroShopExchangeStatus? _current;
    internal PoroShopExchangeStatus? Current { get { lock (_gate) return _current; } }
    internal event Action<PoroShopExchangeStatus>? Changed;
    internal Task Completion { get; private set; } = Task.CompletedTask;

    internal bool TryStart(PoroShopSnapshot preview, IReadOnlyList<PoroShopSelection> selections)
    {
        if (selections.Count == 0 || selections.Any(s => s.Quantity <= 0) ||
            selections.Select(s => s.ItemId).Distinct().Count() != selections.Count) return false;
        var submitted = selections.ToArray();
        PoroShopExchangeStatus status;
        lock (_gate)
        {
            if (_current?.IsRunning == true) return false;
            _current = status = new(preview.AccountKey, true, null);
        }
        Publish(status);
        // 与窗口请求令牌、UI 同步上下文无关；整个任务自行观察并保存异常结果。
        Completion = Task.Run(() => RunAsync(preview, submitted));
        return true;
    }

    private async Task RunAsync(PoroShopSnapshot preview, PoroShopSelection[] selections)
    {
        var results = selections.ToDictionary(s => s.ItemId, s => new PoroShopItemResult(s.ItemId,
            preview.Items.FirstOrDefault(i => i.Id == s.ItemId)?.Name ?? s.ItemId,
            s.Quantity, 0, PoroShopExchangeState.Skipped, "尚未提交"));
        PoroShopSelection[] pending = [];
        try
        {
            // 保留底层每批 100 份的校验，全量兑换按顺序分批；任一批未确认即停止。
            foreach (var batch in Batches(selections))
            {
                pending = batch;
                var result = await features.ExchangePoroShopItemsAsync(preview, batch, CancellationToken.None).ConfigureAwait(false);
                foreach (var selection in batch)
                {
                    var item = result.Items.FirstOrDefault(i => i.ItemId == selection.ItemId);
                    var previous = results[selection.ItemId];
                    results[selection.ItemId] = previous with
                    {
                        Succeeded = previous.Succeeded + (item?.Succeeded ?? 0),
                        State = item?.State ?? PoroShopExchangeState.Unconfirmed,
                        Message = item?.Message ?? result.Message
                    };
                }
                pending = [];
                if (batch.Any(s => result.Items.FirstOrDefault(i => i.ItemId == s.ItemId) is not { State: PoroShopExchangeState.Succeeded } item || item.Succeeded != s.Quantity))
                    break;
            }
        }
        catch (Exception)
        {
            foreach (var selection in pending)
                results[selection.ItemId] = results[selection.ItemId] with
                {
                    State = PoroShopExchangeState.Unconfirmed,
                    Message = "兑换结果未确认，请在客户端核对；未自动重试"
                };
        }
        var items = selections.Select(s => results[s.ItemId]).Select(item =>
            item.Succeeded < item.Requested && item.State == PoroShopExchangeState.Succeeded
                ? item with { State = PoroShopExchangeState.Skipped, Message = "已完成部分数量，后续兑换已停止" } : item).ToArray();
        long completed = items.Sum(i => (long)i.Succeeded);
        int incomplete = items.Count(i => i.State != PoroShopExchangeState.Succeeded || i.Succeeded != i.Requested);
        var status = new PoroShopExchangeStatus(preview.AccountKey, false,
            new(items, incomplete == 0 ? $"兑换成功，共 {completed} 份" : $"已确认兑换 {completed} 份；{incomplete} 项未全部完成，请核对结果"));
        lock (_gate) _current = status;
        Publish(status);
    }

    private void Publish(PoroShopExchangeStatus status)
    {
        // 一个已关闭窗口或通知订阅者的异常不能使后台任务丢失结果。
        foreach (Action<PoroShopExchangeStatus> handler in Changed?.GetInvocationList() ?? [])
            try { handler(status); } catch { }
    }

    private static IEnumerable<PoroShopSelection[]> Batches(IReadOnlyList<PoroShopSelection> selections)
    {
        var batch = new List<PoroShopSelection>();
        int units = 0;
        foreach (var selection in selections)
        {
            int remaining = selection.Quantity;
            while (remaining > 0)
            {
                int count = Math.Min(remaining, 100 - units);
                batch.Add(new(selection.ItemId, count)); units += count; remaining -= count;
                if (units == 100) { yield return batch.ToArray(); batch.Clear(); units = 0; }
            }
        }
        if (batch.Count > 0) yield return batch.ToArray();
    }
}
