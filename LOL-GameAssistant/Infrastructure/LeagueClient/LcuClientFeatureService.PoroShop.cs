using LOL_GameAssistant.Application.ClientFeatures;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

public sealed partial class LcuClientFeatureService
{
    private sealed record PoroShopOffer(PoroShopItem Item, string Plug, string App, string Type);
    private sealed record PoroShopData(PoroShopSnapshot Snapshot, MallSession Session, IReadOnlyList<PoroShopOffer> Offers);

    public async Task<PoroShopSnapshot> GetPoroShopAsync(CancellationToken cancellationToken = default) =>
        (await ReadPoroShopAsync(cancellationToken).ConfigureAwait(false)).Snapshot;

    private async Task<PoroShopData> ReadPoroShopAsync(CancellationToken token)
    {
        var session = await GetMallSession(token).ConfigureAwait(false);
        ValidatePoroIdentity(session, await _poroRpc.GetIdentityAsync(token).ConfigureAwait(false));
        string area = session.Area.ToString(CultureInfo.InvariantCulture);
        var wallet = await PoroShopRequestAsync(session, new() { ["_service"] = "welink.usertask.QueryLolCloudTicket",
            ["acctid"] = "A100213", ["id"] = "1,3", ["area"] = area + "," + area, ["level"] = "1" }, token).ConfigureAwait(false);
        if (wallet["ret_code"]?.ToString() != "0" || !ReadNonNegative(wallet.SelectToken("data.1"), out long silver)
            || !ReadNonNegative(wallet.SelectToken("data.3"), out long gold)
            || !ReadNonNegative(wallet.SelectToken("data.summonerLevel"), out long level))
            throw new InvalidOperationException("未取得魄罗商店余额，请刷新后重试");
        var catalog = await PoroShopRequestAsync(session, new() { ["_service"] = "goods.lol.judou.limit",
            ["_act_id"] = "4719", ["_dh_act_id"] = "4718", ["biz"] = "lol", ["_zone_id"] = area }, token).ConfigureAwait(false);
        if (catalog["result"]?.ToString() != "0" || catalog.SelectToken("dh_data.goods") is not JArray goods
            || !ReadNonNegative(catalog["serverTime"], out long seconds)) throw new InvalidOperationException("魄罗商店目录暂不可用");
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(seconds);
        var offers = new List<PoroShopOffer>();
        foreach (var row in goods.OfType<JObject>())
        {
            string type = row["iType"]?.ToString() ?? "";
            // 官方 38/42 为确定物品兑换，39 为抽取，不进入多选兑换。
            if (type is not ("38" or "42")) continue;
            string id = row["iGoodsId"]?.ToString() ?? "", plug = row["iPlugId"]?.ToString() ?? "", app = row["sAppIds"]?.ToString() ?? "";
            if (id.Length == 0 || !id.All(char.IsAsciiDigit) || plug.Length == 0 || !plug.All(char.IsAsciiDigit) || app != "1003"
                || !ReadNonNegative(row["iPrice"], out long price)) throw new InvalidOperationException("魄罗商品信息格式变化");
            var currency = type == "42" ? PoroShopCurrency.Gold : PoroShopCurrency.Silver;
            int remaining = int.MaxValue;
            bool knownLimit = row["limit"] is JArray limits && limits.Count > 0;
            if (knownLimit)
                foreach (var limit in (JArray)row["limit"]!)
                {
                    if (!ReadNonNegative(limit["left"], out long left) || left > int.MaxValue) { knownLimit = false; break; }
                    remaining = Math.Min(remaining, (int)left);
                }
            if (!knownLimit) remaining = 0;
            bool active = ShopDate(row["dtBeginTime"]?.ToString()) is { } begin && ShopDate(row["dtEndTime"]?.ToString()) is { } end
                && now >= begin && now <= end;
            string reason = !active ? "未开放或已结束" : !knownLimit ? "限购状态待确认" : remaining == 0 ? "已达兑换上限"
                : level <= 4 && id is "402" or "404" or "406" ? "等级条件未满足"
                : price > (currency == PoroShopCurrency.Silver ? silver : gold) ? "余额不足" : "可兑换";
            offers.Add(new(new(id, row.Value<string>("sGoodsName") ?? id, row.Value<string>("sGoodsDesc") ?? "", price,
                currency, remaining, reason == "可兑换", reason), plug, app, type));
        }
        if (offers.Select(o => o.Item.Id).Distinct().Count() != offers.Count) throw new InvalidOperationException("魄罗商品标识重复");
        string account = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(session.Uin + ":" + session.Platform)));
        return new(new(account, silver, gold, offers.Select(o => o.Item).ToArray(), "每次兑换会消耗对应的魄罗赏银或赏金"), session, offers);
    }

    public async Task<PoroShopBatchResult> ExchangePoroShopItemsAsync(PoroShopSnapshot preview, IReadOnlyList<PoroShopSelection> selections,
        CancellationToken cancellationToken = default)
    {
        await _rewardGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<PoroShopItemResult>();
        bool inFlight = false;
        PoroShopSelection? current = null;
        int completed = 0;
        try
        {
            if (selections.Count == 0 || selections.Count > 100 || selections.Any(s => s.Quantity is < 1 or > 100)
                || selections.Select(s => s.ItemId).Distinct().Count() != selections.Count || selections.Sum(s => s.Quantity) > 100)
                return RejectShopBatch(preview, selections, "请勾选商品并设置有效数量，单批最多 100 份，不能重复商品");
            var fresh = await ReadPoroShopAsync(cancellationToken).ConfigureAwait(false);
            if (fresh.Snapshot.AccountKey != preview.AccountKey)
                return RejectShopBatch(preview, selections, "账号或区服已变化，请刷新商店后重新勾选");
            long silver = 0, gold = 0;
            foreach (var selection in selections)
            {
                var item = fresh.Snapshot.Items.SingleOrDefault(i => i.Id == selection.ItemId);
                var expected = preview.Items.SingleOrDefault(i => i.Id == selection.ItemId);
                if (item == null || expected == null || !item.CanExchange || item.Remaining < selection.Quantity
                    || item.UnitPrice != expected.UnitPrice || item.Currency != expected.Currency)
                    return RejectShopBatch(preview, selections, "商品价格、条件或限购已变化，请刷新商店后重新勾选");
                if (item.Currency == PoroShopCurrency.Silver) silver = checked(silver + checked(item.UnitPrice * selection.Quantity));
                else gold = checked(gold + checked(item.UnitPrice * selection.Quantity));
            }
            if (silver > fresh.Snapshot.Silver || gold > fresh.Snapshot.Gold)
                return RejectShopBatch(preview, selections, "所选商品总价超过当前余额，未执行兑换");
            foreach (var selection in selections)
            {
                current = selection; completed = 0;
                var expected = preview.Items.Single(i => i.Id == selection.ItemId);
                for (int unit = 0; unit < selection.Quantity; unit++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    fresh = await ReadPoroShopAsync(cancellationToken).ConfigureAwait(false);
                    var offer = fresh.Offers.SingleOrDefault(o => o.Item.Id == selection.ItemId);
                    if (fresh.Snapshot.AccountKey != preview.AccountKey || offer == null || !offer.Item.CanExchange
                        || offer.Item.UnitPrice != expected.UnitPrice || offer.Item.Currency != expected.Currency)
                        throw new InvalidOperationException("账号、价格、余额或兑换条件已变化");
                    if (fresh.Session != await GetMallSession(cancellationToken).ConfigureAwait(false))
                        throw new InvalidOperationException("客户端登录态已变化");
                    inFlight = true;
                    var response = await PoroShopRequestAsync(fresh.Session, new() { ["_service"] = "buy.plug.swoole.loljf",
                        ["p_tk"] = ShopCsrf(fresh.Session), ["iZone"] = fresh.Session.Area.ToString(CultureInfo.InvariantCulture),
                        ["_plug_id"] = offer.Plug, ["_app_id"] = offer.App, ["iGoodsSeqId"] = offer.Item.Id, ["iActionType"] = offer.Type }, cancellationToken).ConfigureAwait(false);
                    if (!long.TryParse(response["ret"]?.ToString(), out long businessCode))
                        throw new InvalidOperationException("兑换业务结果未确认");
                    if (businessCode != 0)
                    {
                        inFlight = false;
                        results.Add(new(selection.ItemId, expected.Name, selection.Quantity, completed, PoroShopExchangeState.Rejected,
                            "服务端拒绝本项兑换，剩余批次已停止"));
                        return FinishShopBatch(preview, selections, results);
                    }
                    // 接受请求后核对同商品限购和币种余额，不把 HTTP 200 当成到账。
                    var after = await ReadPoroShopAsync(cancellationToken).ConfigureAwait(false);
                    var latest = after.Snapshot.Items.SingleOrDefault(i => i.Id == selection.ItemId);
                    long beforeBalance = offer.Item.Currency == PoroShopCurrency.Silver ? fresh.Snapshot.Silver : fresh.Snapshot.Gold;
                    long afterBalance = offer.Item.Currency == PoroShopCurrency.Silver ? after.Snapshot.Silver : after.Snapshot.Gold;
                    if (after.Snapshot.AccountKey != preview.AccountKey || latest == null || latest.Remaining != offer.Item.Remaining - 1
                        || afterBalance != beforeBalance - offer.Item.UnitPrice)
                        throw new InvalidOperationException("兑换请求已被接受，状态尚未确认");
                    completed++; inFlight = false;
                }
                results.Add(new(selection.ItemId, expected.Name, selection.Quantity, completed, PoroShopExchangeState.Succeeded, "已核对兑换成功"));
                current = null;
            }
            return FinishShopBatch(preview, selections, results);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && !inFlight && results.Count == 0 && completed == 0) { throw; }
        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException or OperationCanceledException
            or WebSocketException or FormatException or ArgumentException or OverflowException)
        {
            if (current == null && results.Count == 0)
                return RejectShopBatch(preview, selections, "商店读取或校验失败，未执行兑换，请刷新后重试");
            if (current != null)
                results.Add(new(current.ItemId, preview.Items.FirstOrDefault(i => i.Id == current.ItemId)?.Name ?? current.ItemId,
                    current.Quantity, completed, inFlight ? PoroShopExchangeState.Unconfirmed : PoroShopExchangeState.Rejected,
                    inFlight ? "本次兑换结果未确认，已停止后续兑换，请在客户端核对；未自动重试" : "账号、余额、商品状态或接口发生变化，已停止后续兑换"));
            return FinishShopBatch(preview, selections, results);
        }
        finally { _rewardGate.Release(); }
    }

    private static PoroShopBatchResult RejectShopBatch(PoroShopSnapshot preview, IReadOnlyList<PoroShopSelection> selections, string reason) =>
        new(selections.Select(s => new PoroShopItemResult(s.ItemId, preview.Items.FirstOrDefault(i => i.Id == s.ItemId)?.Name ?? s.ItemId,
            s.Quantity, 0, PoroShopExchangeState.Rejected, reason)).ToArray(), reason);
    private static PoroShopBatchResult FinishShopBatch(PoroShopSnapshot preview, IReadOnlyList<PoroShopSelection> selections, List<PoroShopItemResult> results)
    {
        foreach (var selection in selections.Where(s => results.All(r => r.ItemId != s.ItemId)))
            results.Add(new(selection.ItemId, preview.Items.FirstOrDefault(i => i.Id == selection.ItemId)?.Name ?? selection.ItemId,
                selection.Quantity, 0, PoroShopExchangeState.Skipped, "本批已停止，未提交"));
        return new(results, $"已确认兑换 {results.Sum(r => r.Succeeded)} 份；{results.Count(r => r.State != PoroShopExchangeState.Succeeded)} 项未全部完成");
    }
    private static bool ReadNonNegative(JToken? value, out long number) => long.TryParse(value?.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out number) && number >= 0;
    private static DateTimeOffset? ShopDate(string? value) => DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out var date) ? new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), TimeSpan.FromHours(8)) : null;
    private static string ShopCsrf(MallSession session)
    {
        uint hash = 5381;
        foreach (char ch in session.Cookie[(session.Cookie.IndexOf("skey=", StringComparison.Ordinal) + 5)..]) hash = unchecked(hash * 33 + ch);
        return (hash & 0x7fffffff).ToString(CultureInfo.InvariantCulture);
    }
    private async Task<JObject> PoroShopRequestAsync(MallSession session, Dictionary<string, string> fields, CancellationToken token)
    {
        fields["_biz_code"] = "lol";
        string query = string.Join("&", fields.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://apps.game.qq.com/daoju/igw/main/?" + query);
        request.Headers.TryAddWithoutValidation("Cookie", session.Cookie);
        request.Headers.Referrer = new Uri("https://lol.qq.com/taskcenter/view/dailytask/luckmall.html");
        using var response = await _rewardHttp.SendAsync(request, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("魄罗商店接口暂不可用");
        return JObject.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
    }
}
