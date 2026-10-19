using LOL_GameAssistant.Domain.Builds;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

public sealed partial class OpggBuildApplyService
{
    private async Task<string> ApplyPersonalItemsAsync(PersonalRunePreset preset, CancellationToken token)
    {
        if (preset.ItemBlocks is not { Count: > 0 }) return "方案未记录装备，保留现有物品集";
        if (preset.ItemBlocks.Any(b => b.ItemIds is not { Count: > 0 } || b.ItemIds.Any(id => id <= 0))) throw new InvalidOperationException("个人装备块含无效项目，未写入装备");
        var self = await ReadObjectAsync("/lol-summoner/v1/current-summoner", token).ConfigureAwait(false);
        if (self?.Value<long?>("summonerId") is not > 0) throw new InvalidOperationException("未确认当前玩家，无法写入个人装备");
        string endpoint = $"/lol-item-sets/v1/item-sets/{self.Value<long>("summonerId")}/sets";
        var wrapper = await ReadObjectAsync(endpoint, token).ConfigureAwait(false);
        if (wrapper?["itemSets"] is not JArray existing) throw new InvalidOperationException("无法读取现有物品集，已停止写入");
        string title = PersonalRunePrefix + preset.Name;
        var retained = existing.OfType<JObject>().Where(s => s.Value<string>("title") != title || s["associatedChampions"]?.Values<int>().Contains(preset.ChampionId) != true).Select(s => s.DeepClone()).ToList();
        retained.Add(new JObject { ["uid"] = Guid.NewGuid().ToString(), ["title"] = title, ["type"] = "custom", ["mode"] = "any", ["map"] = "any",
            ["associatedChampions"] = new JArray(preset.ChampionId), ["associatedMaps"] = new JArray(11, 12),
            ["blocks"] = new JArray(preset.ItemBlocks.Select(b => BuildBlock(b.Title, b.ItemIds))), ["preferredItemSlots"] = new JArray(), ["sortrank"] = 0, ["startedFrom"] = "blank" });
        var body = new JObject { ["accountId"] = wrapper["accountId"] ?? self["accountId"], ["itemSets"] = new JArray(retained), ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        if (!await _lcu.PutAsync(endpoint, body.ToString(Formatting.None), token).ConfigureAwait(false)) throw new InvalidOperationException("符文可能已写入，但客户端拒绝个人装备方案；可使用应用前快照恢复");
        return $"已写入 {preset.ItemBlocks.Count} 组个人装备";
    }
}
