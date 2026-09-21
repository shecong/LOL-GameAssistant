using System.Net.Http.Headers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.LeagueClient;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>
/// OP.GG → LCU 的一键配置实现。
/// 数据来自 OP.GG 的公开英雄接口；写入仅使用本机 LCU 的符文页与自定义物品集端点。
/// 不读取游戏内存、不注入游戏进程，也不会删除用户创建的符文页或物品集。
/// </summary>
public sealed class OpggBuildApplyService : IOpggBuildApplyService
{
    private const string ManagedRunePrefix = "LOL助手 OP.GG · ";
    private const string ManagedItemPrefix = "LOL助手 OP.GG · ";
    private static readonly HttpClient OpggHttp = CreateHttpClient();

    private readonly ILcuRequestSender _lcu;
    private readonly IChampionCatalog _championCatalog;

    public OpggBuildApplyService(ILcuRequestSender lcu, IChampionCatalog championCatalog)
    {
        _lcu = lcu;
        _championCatalog = championCatalog;
    }

    public async Task<OpggBuildChoices> GetBuildChoicesAsync(
        int championId,
        string? position,
        CancellationToken cancellationToken = default)
    {
        if (championId <= 0)
            return OpggBuildChoices.Failure("未识别到当前英雄。请在选择英雄并锁定后再试。");

        try
        {
            string role = NormalizePosition(position);
            IReadOnlyList<OpggBuildOption> options = await FetchBuildOptionsAsync(championId, role, cancellationToken).ConfigureAwait(false);
            if (options.Count == 0)
                return OpggBuildChoices.Failure("OP.GG 没有返回至少三件核心装备的可选方案。");

            string championName = _championCatalog.GetDisplayName(championId);
            if (string.IsNullOrWhiteSpace(championName)) championName = $"英雄{championId}";
            return new OpggBuildChoices(true, "请选择要应用的出装路线。", championName, GetPositionName(role), options);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return OpggBuildChoices.Failure("无法连接 OP.GG 推荐数据，请检查网络后重试。");
        }
        catch (JsonException)
        {
            return OpggBuildChoices.Failure("OP.GG 返回的数据格式已变化，本次未写入客户端。");
        }
        catch (InvalidOperationException ex)
        {
            return OpggBuildChoices.Failure(ex.Message);
        }
        catch
        {
            return OpggBuildChoices.Failure("获取 OP.GG 出装失败，请稍后重试。");
        }
    }

    public async Task<OpggBuildApplyResult> ApplyBuildAsync(
        int championId,
        string? position,
        OpggBuildOption option,
        CancellationToken cancellationToken = default)
    {
        if (championId <= 0)
            return OpggBuildApplyResult.Failure("未识别到当前英雄。请在选择英雄并锁定后再试。");
        if (option.RunePerkIds.Count < 6)
            return OpggBuildApplyResult.Failure("所选方案缺少完整符文数据，本次未写入客户端。");
        if (option.CoreItemIds.Count < 3)
            return OpggBuildApplyResult.Failure("所选方案没有至少三件核心装备，本次未写入客户端。");

        try
        {
            string role = NormalizePosition(position);
            string championName = _championCatalog.GetDisplayName(championId);
            if (string.IsNullOrWhiteSpace(championName)) championName = $"英雄{championId}";
            string label = $"{championName} {GetPositionName(role)} · 方案 {option.Order}";
            var build = new OpggBuild(
                option.PrimaryStyleId,
                option.SubStyleId,
                option.RunePerkIds.ToList(),
                option.StarterItemIds.ToList(),
                option.CoreItemIds.ToList(),
                option.SituationalItemIds.ToList());

            string runeResult = await ApplyRunePageAsync(build, label, cancellationToken).ConfigureAwait(false);
            string itemResult = await ApplyItemSetAsync(build, championId, label, cancellationToken).ConfigureAwait(false);
            return OpggBuildApplyResult.Success($"已应用 OP.GG {label}：{runeResult}；{itemResult}。游戏内请在商店的“自定义物品集”查看出装。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            return OpggBuildApplyResult.Failure(ex.Message);
        }
        catch
        {
            return OpggBuildApplyResult.Failure("一键配置失败。请确认 LOL 客户端已登录且正处于可编辑符文的选人阶段。");
        }
    }

    public async Task<OpggBuildApplyResult> ApplyForChampionAsync(
        int championId,
        string? position,
        CancellationToken cancellationToken = default)
    {
        OpggBuildChoices choices = await GetBuildChoicesAsync(championId, position, cancellationToken).ConfigureAwait(false);
        if (!choices.Succeeded) return OpggBuildApplyResult.Failure(choices.Message);
        OpggBuildOption? first = choices.Options.FirstOrDefault();
        return first == null
            ? OpggBuildApplyResult.Failure("OP.GG 没有可应用的出装路线。")
            : await ApplyBuildAsync(championId, position, first, cancellationToken).ConfigureAwait(false);
    }

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LOLGameAssistant", "1.0"));
        return http;
    }

    /// <summary>
    /// OP.GG 的接口接受数值英雄 ID，因此不依赖中文名称到英文 slug 的不稳定映射。
    /// 该接口返回符文、出装、召唤师技能等同一份英雄构建数据。
    /// </summary>
    private static async Task<IReadOnlyList<OpggBuildOption>> FetchBuildOptionsAsync(int championId, string role, CancellationToken cancellationToken)
    {
        string url = $"https://lol-api-champion.op.gg/api/global/champions/ranked/{championId}/{role}";
        using HttpResponseMessage response = await OpggHttp.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("OP.GG 暂无该英雄/位置的推荐数据，请确认选择的位置后重试。");

        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        JObject data = JObject.Parse(json)["data"] as JObject
            ?? throw new JsonException("missing data");
        JObject? rune = data["runes"]?.OfType<JObject>()
            .OrderByDescending(item => item.Value<int?>("play") ?? 0)
            .FirstOrDefault();
        JObject? firstStarter = data["starter_items"]?.OfType<JObject>()
            .OrderByDescending(item => item.Value<int?>("play") ?? 0)
            .FirstOrDefault();
        JObject? firstBoots = data["boots"]?.OfType<JObject>()
            .OrderByDescending(item => item.Value<int?>("play") ?? 0)
            .FirstOrDefault();

        int boot = ReadIds(firstBoots?["ids"]).FirstOrDefault();
        List<int> runePerks = ReadIds(rune?["primary_rune_ids"])
            .Concat(ReadIds(rune?["secondary_rune_ids"]))
            .Concat(ReadIds(rune?["stat_mod_ids"]))
            .ToList();
        if ((rune?.Value<int?>("primary_page_id") ?? 0) <= 0 ||
            (rune?.Value<int?>("secondary_page_id") ?? 0) <= 0 || runePerks.Count < 6)
            throw new InvalidOperationException("OP.GG 没有返回完整符文数据，本次未写入客户端。");

        var allCore = data["core_items"]?.OfType<JObject>()
            .OrderByDescending(item => item.Value<int?>("play") ?? 0)
            .ToList() ?? new List<JObject>();
        var options = new List<OpggBuildOption>();
        foreach (JObject coreVariant in allCore.Where(item => ReadIds(item["ids"]).Count >= 3).Take(5))
        {
            var core = ReadIds(coreVariant["ids"]);
            if (boot > 0 && !core.Contains(boot)) core.Insert(Math.Min(1, core.Count), boot);
            var alternateFrequency = new Dictionary<int, int>();
            foreach (JObject alternate in allCore.Where(item => !ReferenceEquals(item, coreVariant)).Take(8))
            {
                int weight = Math.Max(1, alternate.Value<int?>("play") ?? 1);
                foreach (int id in ReadIds(alternate["ids"]).Where(id => !core.Contains(id)))
                    alternateFrequency[id] = alternateFrequency.GetValueOrDefault(id) + weight;
            }

            int matches = coreVariant.Value<int?>("play") ?? 0;
            options.Add(new OpggBuildOption(
                options.Count + 1,
                ReadIds(firstStarter?["ids"]),
                core,
                alternateFrequency.OrderByDescending(item => item.Value).Select(item => item.Key).Take(6).ToList(),
                rune!.Value<int?>("primary_page_id") ?? 0,
                rune.Value<int?>("secondary_page_id") ?? 0,
                runePerks,
                matches,
                coreVariant.Value<int?>("win") ?? 0));
        }
        return options;
    }

    private async Task<string> ApplyRunePageAsync(OpggBuild build, string label, CancellationToken cancellationToken)
    {
        if (build.PrimaryStyleId <= 0 || build.SubStyleId <= 0)
            throw new InvalidOperationException("OP.GG 符文缺少主系或副系，本次未写入客户端。");

        JArray pages = await ReadArrayAsync("/lol-perks/v1/pages", cancellationToken).ConfigureAwait(false);
        foreach (JObject page in pages.OfType<JObject>()
                     .Where(page => (page.Value<string>("name") ?? "").StartsWith(ManagedRunePrefix, StringComparison.Ordinal))
                     .ToList())
        {
            long? id = page.Value<long?>("id");
            if (id.HasValue)
                await _lcu.DeleteAsync($"/lol-perks/v1/pages/{id.Value}", cancellationToken).ConfigureAwait(false);
        }

        pages = await ReadArrayAsync("/lol-perks/v1/pages", cancellationToken).ConfigureAwait(false);
        JObject? inventory = await ReadObjectAsync("/lol-perks/v1/inventory", cancellationToken).ConfigureAwait(false);
        int pageLimit = inventory?.Value<int?>("ownedPageCount") ?? 2;
        if (pages.Count >= pageLimit)
            throw new InvalidOperationException("符文页数量已满。为保护你的自定义符文页，本助手不会自动删除它们；请先在客户端删除一个页面后重试。");

        string pageName = ManagedRunePrefix + label;
        string body = JsonConvert.SerializeObject(new
        {
            name = pageName,
            primaryStyleId = build.PrimaryStyleId,
            subStyleId = build.SubStyleId,
            selectedPerkIds = build.RunePerks,
            current = true,
            order = 0
        });
        if (!await _lcu.PostAsync("/lol-perks/v1/pages", body, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("客户端拒绝创建符文页。请确认仍处于可编辑符文的阶段。");

        // 少数客户端版本会忽略 POST 的 current 标志，补一次显式切换。
        pages = await ReadArrayAsync("/lol-perks/v1/pages", cancellationToken).ConfigureAwait(false);
        long? createdId = pages.OfType<JObject>()
            .FirstOrDefault(page => string.Equals(page.Value<string>("name"), pageName, StringComparison.Ordinal))?
            .Value<long?>("id");
        if (createdId.HasValue)
            await _lcu.PutAsync("/lol-perks/v1/currentpage", JsonConvert.SerializeObject(new { id = createdId.Value }), cancellationToken)
                .ConfigureAwait(false);
        return "符文页已设为当前";
    }

    private async Task<string> ApplyItemSetAsync(
        OpggBuild build,
        int championId,
        string label,
        CancellationToken cancellationToken)
    {
        JObject? currentSummoner = await ReadObjectAsync("/lol-summoner/v1/current-summoner", cancellationToken).ConfigureAwait(false);
        long? summonerId = currentSummoner?.Value<long?>("summonerId");
        if (!summonerId.HasValue || summonerId <= 0)
            throw new InvalidOperationException("未读取到当前召唤师，无法写入自定义物品集。");

        string endpoint = $"/lol-item-sets/v1/item-sets/{summonerId.Value}/sets";
        JObject? wrapper = await ReadObjectAsync(endpoint, cancellationToken).ConfigureAwait(false);
        var preservedSets = (wrapper?["itemSets"] as JArray ?? new JArray())
            .OfType<JObject>()
            .Where(itemSet => !(itemSet.Value<string>("title") ?? "").StartsWith(ManagedItemPrefix, StringComparison.Ordinal))
            .ToList();

        var newSet = new JObject
        {
            ["uid"] = Guid.NewGuid().ToString(),
            ["title"] = ManagedItemPrefix + label,
            ["type"] = "custom",
            ["mode"] = "any",
            ["map"] = "any",
            ["associatedChampions"] = new JArray(championId),
            ["associatedMaps"] = new JArray(11, 12),
            ["blocks"] = BuildItemBlocks(build),
            ["preferredItemSlots"] = new JArray(),
            ["sortrank"] = 0,
            ["startedFrom"] = "blank"
        };
        preservedSets.Add(newSet);
        string body = new JObject
        {
            ["accountId"] = wrapper?.Value<long?>("accountId") ?? currentSummoner?.Value<long?>("accountId") ?? 0,
            ["itemSets"] = new JArray(preservedSets),
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        }.ToString(Formatting.None);
        if (!await _lcu.PutAsync(endpoint, body, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("客户端拒绝写入自定义物品集。");
        return "出装已写入自定义物品集";
    }

    private static JArray BuildItemBlocks(OpggBuild build)
    {
        var blocks = new JArray();
        if (build.StarterItems.Count > 0)
            blocks.Add(BuildBlock("1. 出门装", build.StarterItems));
        for (int index = 0; index < build.CoreItems.Count; index++)
        {
            string name = index == 1 && IsLikelyBoot(build.CoreItems[index])
                ? "鞋子"
                : $"核心第 {index + 1} 件";
            blocks.Add(BuildBlock($"{index + 2}. {name}", new[] { build.CoreItems[index] }));
        }
        if (build.SituationalItems.Count > 0)
            blocks.Add(BuildBlock("可选 / 反制装备", build.SituationalItems));
        return blocks;
    }

    private static JObject BuildBlock(string title, IEnumerable<int> itemIds) => new()
    {
        ["type"] = title,
        ["items"] = new JArray(itemIds.Where(item => item > 0)
            .GroupBy(item => item)
            .Select(group => new JObject { ["id"] = group.Key.ToString(), ["count"] = group.Count() }))
    };

    private async Task<JObject?> ReadObjectAsync(string endpoint, CancellationToken cancellationToken)
    {
        string? content = await _lcu.GetStringAsync(endpoint, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(content) ? null : JObject.Parse(content);
    }

    private async Task<JArray> ReadArrayAsync(string endpoint, CancellationToken cancellationToken)
    {
        string? content = await _lcu.GetStringAsync(endpoint, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(content) ? new JArray() : JArray.Parse(content);
    }

    private static List<int> ReadIds(JToken? token) => token?.Values<int>()
        .Where(id => id > 0)
        .ToList() ?? new List<int>();

    private static bool IsLikelyBoot(int itemId) => itemId is 3005 or 3006 or 3009 or 3010 or 3020 or 3047 or 3111 or 3117 or 3158;

    private static string NormalizePosition(string? position) => position?.Trim().ToUpperInvariant() switch
    {
        "TOP" => "top",
        "JUNGLE" or "JNG" => "jungle",
        "MIDDLE" or "MID" => "mid",
        "BOTTOM" or "BOT" or "ADC" => "adc",
        "UTILITY" or "SUPPORT" or "SUP" => "support",
        _ => "mid"
    };

    private static string GetPositionName(string position) => position switch
    {
        "top" => "上路",
        "jungle" => "打野",
        "mid" => "中路",
        "adc" => "下路",
        "support" => "辅助",
        _ => position
    };

    private sealed record OpggBuild(
        int PrimaryStyleId,
        int SubStyleId,
        List<int> RunePerks,
        List<int> StarterItems,
        List<int> CoreItems,
        List<int> SituationalItems);
}
