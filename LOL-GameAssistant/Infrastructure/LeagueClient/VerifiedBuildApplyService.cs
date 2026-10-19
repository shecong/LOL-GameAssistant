using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Domain.Builds;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>配置写入串行化、账号隔离快照、应用后核验与手动撤销。不是跨接口事务。</summary>
internal sealed class VerifiedBuildApplyService(IOpggBuildApplyService inner, ILcuRequestSender lcu, string? storage = null) : IOpggBuildApplyService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource _context = new();
    private readonly string _storage = storage ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LOL-GameAssistant", "configuration-snapshots");
    private sealed record Snapshot(string Account, string ItemEndpoint, JArray Pages, JObject? CurrentPage, JObject? Items, JObject? Selection);
    public void ResetClientContext() { var old = Interlocked.Exchange(ref _context, new CancellationTokenSource()); old.Cancel(); }
    public Task<IReadOnlyList<PersonalRunePreset>> GetLocalRunePagesAsync(CancellationToken token = default) => inner.GetLocalRunePagesAsync(token);
    public Task<PersonalRunePreset?> CaptureCurrentRunePresetAsync(int id, string mode, string position, CancellationToken token = default) => inner.CaptureCurrentRunePresetAsync(id, mode, position, token);
    public Task<OpggBuildChoices> GetBuildChoicesAsync(int id, string? position, CancellationToken token = default) => inner.GetBuildChoicesAsync(id, position, token);
    public Task<OpggBuildChoices> GetBuildChoicesAsync(int id, string? position, OpggBuildRequest request, CancellationToken token = default) => inner.GetBuildChoicesAsync(id, position, request, token);
    private async Task<JToken?> Read(string route, CancellationToken token)
    { string? text = await lcu.GetStringAsync(route, token).ConfigureAwait(false); if (string.IsNullOrWhiteSpace(text)) return null; try { return JToken.Parse(text); } catch (JsonException) { return null; } }
    private string FileName(string account) => Path.Combine(_storage, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account))) + ".json");
    private async Task<Snapshot?> Capture(CancellationToken token)
    {
        var account = await Read("/lol-summoner/v1/current-summoner", token).ConfigureAwait(false) as JObject;
        string? puuid = account?.Value<string>("puuid"); long id = account?.Value<long?>("summonerId") ?? 0;
        var phase = (await lcu.GetStringAsync("/lol-gameflow/v1/gameflow-phase", token).ConfigureAwait(false))?.Trim('"');
        if (string.IsNullOrWhiteSpace(puuid) || id <= 0 || phase is not ("Lobby" or "ChampSelect" or "None")) return null;
        var pages = await Read("/lol-perks/v1/pages", token).ConfigureAwait(false) as JArray;
        if (pages == null) return null;
        string endpoint = $"/lol-item-sets/v1/item-sets/{id}/sets";
        return new(puuid, endpoint, pages, await Read("/lol-perks/v1/currentpage", token).ConfigureAwait(false) as JObject,
            await Read(endpoint, token).ConfigureAwait(false) as JObject,
            phase == "ChampSelect" ? await Read("/lol-champ-select/v1/session/my-selection", token).ConfigureAwait(false) as JObject : null);
    }
    private async Task<bool> SameAccount(Snapshot snapshot, CancellationToken token) =>
        (await Read("/lol-summoner/v1/current-summoner", token).ConfigureAwait(false))?.Value<string>("puuid") == snapshot.Account;
    private async Task<OpggBuildApplyResult> Apply(Func<CancellationToken, Task<OpggBuildApplyResult>> action,
        int championId, IReadOnlyList<int> runes, IReadOnlyList<int> items, IReadOnlyList<int> spells, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        using var scope = CancellationTokenSource.CreateLinkedTokenSource(token, _context.Token);
        try
        {
            var snapshot = await Capture(scope.Token).ConfigureAwait(false);
            if (snapshot == null) return OpggBuildApplyResult.Failure("无法确认当前账号、可配置阶段或符文页，未开始写入");
            if (items.Count > 0 && snapshot.Items?["itemSets"] is not JArray) return OpggBuildApplyResult.Failure("物品集现状未读取到，未写入；请稍后重试");
            Directory.CreateDirectory(_storage); string file = FileName(snapshot.Account), temp = file + ".tmp";
            await File.WriteAllTextAsync(temp, JsonConvert.SerializeObject(snapshot), scope.Token).ConfigureAwait(false); File.Move(temp, file, true);
            var result = await action(scope.Token).ConfigureAwait(false);
            if (!await SameAccount(snapshot, scope.Token).ConfigureAwait(false)) return OpggBuildApplyResult.Failure("客户端账号已变化，未把原账号结果当作当前账号成功；可重新选择方案");
            var failed = new List<string>();
            if (runes.Count >= 6)
            {
                var current = await Read("/lol-perks/v1/currentpage", scope.Token).ConfigureAwait(false);
                if (current?["selectedPerkIds"]?.Values<int>().SequenceEqual(runes) != true) failed.Add("符文页");
            }
            if (items.Count > 0)
            {
                var current = await Read(snapshot.ItemEndpoint, scope.Token).ConfigureAwait(false);
                bool verified = (current?["itemSets"] as JArray ?? []).OfType<JObject>()
                    .Where(x => x["associatedChampions"]?.Values<int>().Contains(championId) == true)
                    .Any(x => items.All(id => x["blocks"]?.SelectMany(b => b["items"] ?? new JArray()).Any(i => i.Value<string>("id") == id.ToString()) == true));
                if (!verified) failed.Add("装备方案");
            }
            if (snapshot.Selection != null && spells.Count == 2)
            {
                var current = await Read("/lol-champ-select/v1/session/my-selection", scope.Token).ConfigureAwait(false);
                if (current?.Value<int?>("spell1Id") != spells[0] || current?.Value<int?>("spell2Id") != spells[1]) failed.Add("召唤师技能");
            }
            return failed.Count > 0 ? OpggBuildApplyResult.Failure(result.Message + $"；读取核验未通过：{string.Join("、", failed)}。应用前快照已保留，可手动恢复")
                : result with { Message = result.Message + "；已读取核验，应用前快照已保留" };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { return OpggBuildApplyResult.Failure("配置未完整确认：" + e.Message + "；如已部分写入，可使用应用前快照恢复"); }
        finally { _gate.Release(); }
    }
    public Task<OpggBuildApplyResult> ApplyPersonalRunePresetAsync(PersonalRunePreset preset, CancellationToken token = default) =>
        Apply(t => inner.ApplyPersonalRunePresetAsync(preset, t), preset.ChampionId, preset.RunePerkIds,
            (preset.ItemBlocks ?? []).SelectMany(b => b.ItemIds).ToArray(), preset.SummonerSpellIds, token);
    public Task<OpggBuildApplyResult> ApplyBuildAsync(int id, string? position, OpggBuildOption option, CancellationToken token = default,
        bool allowReplaceCurrentRunePage = false, bool applySummonerSpells = true) =>
        Apply(t => inner.ApplyBuildAsync(id, position, option, t, allowReplaceCurrentRunePage, applySummonerSpells), id, option.RunePerkIds,
            option.CoreItemIds, applySummonerSpells ? option.SummonerSpellIds ?? [] : [], token);
    public async Task<OpggBuildApplyResult> ApplyForChampionAsync(int id, string? position, CancellationToken token = default)
    { var choices = await GetBuildChoicesAsync(id, position, token).ConfigureAwait(false); return choices.Options.FirstOrDefault() is { } option ? await ApplyBuildAsync(id, position, option, token).ConfigureAwait(false) : OpggBuildApplyResult.Failure(choices.Message); }
    public async Task<OpggBuildApplyResult> RestoreLastConfigurationAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        using var scope = CancellationTokenSource.CreateLinkedTokenSource(token, _context.Token);
        try
        {
            var current = await Capture(scope.Token).ConfigureAwait(false); if (current == null) return OpggBuildApplyResult.Failure("当前账号或阶段不允许恢复配置");
            string file = FileName(current.Account); if (!File.Exists(file)) return OpggBuildApplyResult.Failure("当前账号没有应用前快照");
            var saved = JsonConvert.DeserializeObject<Snapshot>(await File.ReadAllTextAsync(file, scope.Token).ConfigureAwait(false));
            if (saved == null || saved.Account != current.Account) return OpggBuildApplyResult.Failure("快照账号不匹配");
            var failures = new List<string>();
            foreach (var page in saved.Pages.OfType<JObject>())
            {
                long id = page.Value<long?>("id") ?? 0; var now = current.Pages.OfType<JObject>().FirstOrDefault(p => p.Value<long?>("id") == id);
                if (id <= 0 || now == null || PageEquals(page, now)) continue;
                if (!await SameAccount(saved, scope.Token).ConfigureAwait(false)) return OpggBuildApplyResult.Failure("账号已变化，已停止恢复");
                if (!await lcu.PutAsync($"/lol-perks/v1/pages/{id}", page.ToString(Formatting.None), scope.Token).ConfigureAwait(false)) failures.Add($"符文页 {id}");
            }
            foreach (var page in current.Pages.OfType<JObject>().Where(p => (p.Value<string>("name") ?? "").StartsWith("LOL助手 ", StringComparison.Ordinal)))
            {
                long id = page.Value<long?>("id") ?? 0;
                if (id > 0 && !saved.Pages.OfType<JObject>().Any(p => p.Value<long?>("id") == id) && !await lcu.DeleteAsync($"/lol-perks/v1/pages/{id}", scope.Token).ConfigureAwait(false)) failures.Add($"新增助手页 {id}");
            }
            if (saved.CurrentPage?.Value<long?>("id") is > 0 && !await lcu.PutAsync("/lol-perks/v1/currentpage", saved.CurrentPage["id"]!.ToString(), scope.Token).ConfigureAwait(false)) failures.Add("当前符文页");
            if (saved.Items != null && !await lcu.PutAsync(saved.ItemEndpoint, saved.Items.ToString(Formatting.None), scope.Token).ConfigureAwait(false)) failures.Add("物品集");
            if (saved.Selection != null && current.Selection != null)
            {
                var body = new JObject { ["spell1Id"] = saved.Selection["spell1Id"], ["spell2Id"] = saved.Selection["spell2Id"] };
                if (!await lcu.PatchAsync("/lol-champ-select/v1/session/my-selection", body.ToString(Formatting.None), scope.Token).ConfigureAwait(false)) failures.Add("召唤师技能");
            }
            var after = await Capture(scope.Token).ConfigureAwait(false);
            if (after == null || after.Account != saved.Account) failures.Add("当前账号核验");
            else
            {
                if (saved.CurrentPage != null && !PageEquals(saved.CurrentPage, after.CurrentPage)) failures.Add("当前符文读取核验");
                if (saved.Items?["itemSets"] is { } items && !JToken.DeepEquals(items, after.Items?["itemSets"])) failures.Add("装备读取核验");
                if (saved.Selection != null && after.Selection != null && (saved.Selection.Value<int?>("spell1Id") != after.Selection.Value<int?>("spell1Id") || saved.Selection.Value<int?>("spell2Id") != after.Selection.Value<int?>("spell2Id"))) failures.Add("技能读取核验");
            }
            return failures.Count == 0 ? OpggBuildApplyResult.Success("已恢复当前账号的应用前配置并核验；快照仍保留") : OpggBuildApplyResult.Failure("部分恢复未确认：" + string.Join("、", failures.Distinct()));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { return OpggBuildApplyResult.Failure("恢复失败：" + e.Message); }
        finally { _gate.Release(); }
    }
    private static bool PageEquals(JObject? a, JObject? b) => a != null && b != null &&
        new[] { "id", "name", "primaryStyleId", "subStyleId", "selectedPerkIds" }.All(k => JToken.DeepEquals(a[k], b[k]));
}
