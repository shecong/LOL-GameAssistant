using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.LeagueClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>
/// OP.GG → LCU 的一键配置实现。
/// 数据来自 OP.GG 的公开英雄接口；写入仅使用本机 LCU 的符文页与自定义物品集端点。
/// 不读取游戏内存、不注入游戏进程。仅当用户主动应用方案且符文页已满时，
/// 会删除当前正在使用的符文页以腾出一个位置；不会清理其它自定义页或物品集。
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
        return await GetBuildChoicesAsync(
            championId,
            position,
            new OpggBuildRequest("CLASSIC"),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<OpggBuildChoices> GetBuildChoicesAsync(
        int championId,
        string? position,
        OpggBuildRequest request,
        CancellationToken cancellationToken = default)
    {
        if (championId <= 0)
            return OpggBuildChoices.Failure("未识别到当前英雄。请在选择英雄并锁定后再试。");

        try
        {
            string role = NormalizePosition(position);
            string mode = NormalizeMode(request.GameMode, request.QueueId);
            if (mode == "unknown")
                return OpggBuildChoices.Failure("尚未识别当前对局模式，无法确认 OP.GG 方案适用性。请在选人界面稍后重试。");
            if (mode == "aram_mayhem")
                return OpggBuildChoices.Failure("当前为海克斯大乱斗；OP.GG 的公开推荐接口暂未提供该模式数据，已停止普通大乱斗方案的错误套用。");
            OpggBuildPayload payload = await FetchBuildPayloadAsync(championId, role, mode, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<OpggBuildOption> options = payload.Options;
            if (options.Count == 0)
                return OpggBuildChoices.Failure("OP.GG 没有返回可用的核心出装方案。");

            string championName = _championCatalog.GetDisplayName(championId);
            if (string.IsNullOrWhiteSpace(championName)) championName = $"英雄{championId}";
            string positionName = mode == "ranked" ? GetPositionName(role) : GetModeName(mode);
            return new OpggBuildChoices(
                true,
                "请选择要应用的出装路线。召唤师技能会随方案写入；海克斯与对位仅供展示参考。",
                championName,
                positionName,
                options,
                mode,
                payload.Augments,
                payload.Matchups,
                championId);
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
        if (option.CoreItemIds.Count < 3)
            return OpggBuildApplyResult.Failure("所选方案没有至少三件核心装备，本次未写入客户端。");

        try
        {
            string role = NormalizePosition(position);
            string championName = _championCatalog.GetDisplayName(championId);
            if (string.IsNullOrWhiteSpace(championName)) championName = $"英雄{championId}";
            string contextName = option.Mode == "ranked"
                ? GetPositionName(role) : GetModeName(option.Mode);
            string label = $"{championName} {contextName} · 方案 {option.Order}";
            var build = new OpggBuild(
                option.PrimaryStyleId,
                option.SubStyleId,
                option.RunePerkIds.ToList(),
                option.StarterItemIds.ToList(),
                option.CoreItemIds.ToList(),
                option.SituationalItemIds.ToList());

            string runeResult = option.RunePerkIds.Count >= 6
                ? await ApplyRunePageAsync(build, label, cancellationToken).ConfigureAwait(false)
                : "该模式未提供可写入的符文数据，已保留客户端当前符文";
            string itemResult = await ApplyItemSetAsync(build, championId, label, cancellationToken).ConfigureAwait(false);
            string spellResult = await ApplySummonerSpellsAsync(option.SummonerSpellIds, cancellationToken).ConfigureAwait(false);
            return OpggBuildApplyResult.Success($"已应用 OP.GG {label}：{runeResult}；{itemResult}；{spellResult}。游戏内请在商店的“自定义物品集”查看出装。");
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
    /// OP.GG 使用数值英雄 ID，且不同模式仅路径结构不同：ARAM/URF/闪击使用 none 位置，
    /// 斗魂竞技场没有位置段。接口内的召唤师技能、海克斯与对位数据一起规整为可展示模型。
    /// </summary>
    private static async Task<OpggBuildPayload> FetchBuildPayloadAsync(
        int championId,
        string role,
        string mode,
        CancellationToken cancellationToken)
    {
        string path = mode == "arena"
            ? $"/api/global/champions/{mode}/{championId}"
            : $"/api/global/champions/{mode}/{championId}/{(mode == "ranked" ? role : "none")}";
        string tier = mode == "arena" ? "all" : "gold_plus";
        string url = "https://lol-api-champion.op.gg" + path + "?tier=" + tier;
        using HttpResponseMessage response = await OpggHttp.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("OP.GG 暂无该英雄/模式的推荐数据，请确认当前模式后重试。");

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
        IReadOnlyList<int> summonerSpells = ReadIds(data["summoner_spells"]?.OfType<JObject>()
            .OrderByDescending(item => item.Value<int?>("play") ?? 0)
            .FirstOrDefault()?["ids"]);

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
                rune?.Value<int?>("primary_page_id") ?? 0,
                rune?.Value<int?>("secondary_page_id") ?? 0,
                runePerks,
                matches,
                coreVariant.Value<int?>("win") ?? 0,
                summonerSpells,
                mode));
        }
        var augments = data["augment_group"]?.OfType<JObject>()
            .SelectMany(group => group["augments"]?.OfType<JObject>()
                .Select(augment => new OpggAugmentRecommendation(
                    augment.Value<int?>("id") ?? 0,
                    group.Value<int?>("rarity") ?? 0,
                    augment.Value<int?>("play") ?? 0,
                    augment.Value<int?>("win") ?? 0)) ?? Enumerable.Empty<OpggAugmentRecommendation>())
            .Where(augment => augment.Id > 0)
            .OrderByDescending(augment => augment.Matches)
            .Take(15)
            .ToList() ?? new List<OpggAugmentRecommendation>();
        var matchups = data["counters"]?.OfType<JObject>()
            .Select(counter => new OpggMatchup(
                counter.Value<int?>("champion_id") ?? 0,
                counter.Value<int?>("play") ?? 0,
                counter.Value<int?>("win") ?? 0))
            .Where(matchup => matchup.ChampionId > 0 && matchup.Matches > 0)
            .OrderByDescending(matchup => matchup.Matches)
            .Take(12)
            .ToList() ?? new List<OpggMatchup>();
        return new OpggBuildPayload(options, augments, matchups);
    }

    private async Task<string> ApplySummonerSpellsAsync(
        IReadOnlyList<int>? spellIds,
        CancellationToken cancellationToken)
    {
        int[] spells = spellIds?.Where(id => id > 0).Distinct().Take(2).ToArray() ?? Array.Empty<int>();
        if (spells.Length < 2) return "该模式未提供可写入的召唤师技能，已保留当前技能";

        bool updated = await _lcu.PatchAsync(
            "/lol-champ-select/v1/session/my-selection",
            JsonConvert.SerializeObject(new { spell1Id = spells[0], spell2Id = spells[1] }),
            cancellationToken).ConfigureAwait(false);
        return updated ? "召唤师技能已写入" : "客户端未写入召唤师技能，已保留当前技能";
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
        int customPageCount = inventory?.Value<int?>("customPageCount") ?? CountCustomPages(pages);
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

        if (customPageCount < pageLimit)
        {
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

        // 自定义符文页额度已满：就地改写当前正在使用的符文页，不删除任何页面。
        // 这里不能拿 pages.Count 与 ownedPageCount 比较：选人阶段客户端自建的临时页也在 pages 里，
        // 会让页数虚高、误判“已满”而去删用户的页面；而且删完再比较必然仍然“已满”，
        // 结果是删了页却什么都没写入。
        JObject? currentPage = pages.OfType<JObject>().FirstOrDefault(IsCurrentRunePage);
        long? currentPageId = currentPage?.Value<long?>("id");
        if (!currentPageId.HasValue)
            throw new InvalidOperationException($"自定义符文页已满（{customPageCount}/{pageLimit}），且未识别到当前符文页；请先在客户端释放一个符文页后重试。");

        if (currentPage?.Value<bool?>("isEditable") == false)
            throw new InvalidOperationException($"自定义符文页已满（{customPageCount}/{pageLimit}），且当前符文页不可编辑；请先在客户端释放一个符文页后重试。");

        if (!await _lcu.PutAsync($"/lol-perks/v1/pages/{currentPageId.Value}", body, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException($"自定义符文页已满（{customPageCount}/{pageLimit}），客户端拒绝改写当前符文页；请先在客户端释放一个符文页后重试。");

        // 与新建路径一样：个别版本会忽略 body 里的 current 标志，补一次显式切换。
        await _lcu.PutAsync("/lol-perks/v1/currentpage", JsonConvert.SerializeObject(new { id = currentPageId.Value }), cancellationToken)
            .ConfigureAwait(false);

        return $"符文页已设为当前（自定义符文页已满 {customPageCount}/{pageLimit}，已改写当前使用的符文页）";
    }

    /// <summary>自定义符文页数量；客户端未提供该字段时按“非临时页”估算。</summary>
    private static int CountCustomPages(JArray pages) =>
        pages.OfType<JObject>().Count(page => page.Value<bool?>("isTemporary") != true);

    private static bool IsCurrentRunePage(JObject page) =>
        page.Value<bool?>("current") == true || page.Value<bool?>("isActive") == true;

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

    internal static string NormalizeMode(string? gameMode, int queueId)
    {
        string mode = (gameMode ?? "").Trim().ToUpperInvariant();
        if (mode.StartsWith("KIWI", StringComparison.Ordinal)) return "aram_mayhem";
        if (queueId > 0)
            return queueId switch
            {
                2400 => "aram_mayhem",
                450 => "aram",
                1700 or 1701 or 1704 or 1710 => "arena",
                1300 => "nexus_blitz",
                900 or 1900 => "urf",
                400 or 420 or 430 or 440 or 490 => "ranked",
                _ => "unknown"
            };
        if (mode == "ARAM") return "aram";
        if (mode is "CHERRY" or "ARENA") return "arena";
        if (mode is "NEXUSBLITZ" or "NEXUS_BLITZ") return "nexus_blitz";
        if (mode is "URF" or "ARURF") return "urf";
        if (mode is "CLASSIC" or "CLASSIC SR") return "ranked";
        return "unknown";
    }

    private static string GetModeName(string mode) => mode switch
    {
        "aram" => "极地大乱斗",
        "aram_mayhem" => "海克斯大乱斗",
        "arena" => "斗魂竞技场",
        "nexus_blitz" => "极限闪击",
        "urf" => "无限火力",
        _ => "召唤师峡谷"
    };

    private sealed record OpggBuild(
        int PrimaryStyleId,
        int SubStyleId,
        List<int> RunePerks,
        List<int> StarterItems,
        List<int> CoreItems,
        List<int> SituationalItems);

    private sealed record OpggBuildPayload(
        IReadOnlyList<OpggBuildOption> Options,
        IReadOnlyList<OpggAugmentRecommendation> Augments,
        IReadOnlyList<OpggMatchup> Matchups);
}
