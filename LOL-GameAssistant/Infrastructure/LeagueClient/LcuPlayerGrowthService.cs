using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Domain.Matches;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

public sealed class LcuPlayerGrowthService(IMatchHistoryService matches, IChampionCatalog champions, ILcuRequestSender lcu,
    string? storage = null, bool allowInventory = true) : IPlayerGrowthService
{
    private readonly SemaphoreSlim _files = new(1, 1);
    private readonly string _storage = storage ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LOL-GameAssistant", "training");
    private string FileName(string puuid) => Path.Combine(_storage, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(puuid))) + ".json");
    private async Task<HashSet<int>> ReadTraining(string puuid, CancellationToken token)
    {
        try { return JsonSerializer.Deserialize<HashSet<int>>(await File.ReadAllTextAsync(FileName(puuid), token).ConfigureAwait(false)) ?? []; }
        catch (Exception e) when (e is IOException or JsonException) { return []; }
    }
    public async Task SetTrainingAsync(string puuid, int championId, bool selected, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(puuid) || championId <= 0) throw new ArgumentException("无效玩家或英雄");
        await _files.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var ids = await ReadTraining(puuid, token).ConfigureAwait(false); if (selected) ids.Add(championId); else ids.Remove(championId);
            Directory.CreateDirectory(_storage); string file = FileName(puuid), temp = file + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(ids), token).ConfigureAwait(false); File.Move(temp, file, true);
        }
        finally { _files.Release(); }
    }
    private async Task<Dictionary<long, string>> ReadNotes(string puuid, CancellationToken token)
    {
        try { return JsonSerializer.Deserialize<Dictionary<long, string>>(await File.ReadAllTextAsync(FileName(puuid) + ".reviews", token).ConfigureAwait(false)) ?? []; }
        catch (Exception e) when (e is IOException or JsonException) { return []; }
    }
    public async Task SetReviewNoteAsync(string puuid, long gameId, string note, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(puuid) || gameId <= 0 || note.Length > 500) throw new ArgumentException("请填写不超过 500 字的复盘笔记");
        await _files.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var notes = await ReadNotes(puuid, token).ConfigureAwait(false); if (string.IsNullOrWhiteSpace(note)) notes.Remove(gameId); else notes[gameId] = note.Trim();
            Directory.CreateDirectory(_storage); string target = FileName(puuid) + ".reviews", temp = target + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(notes), token).ConfigureAwait(false); File.Move(temp, target, true);
        }
        finally { _files.Release(); }
    }
    public async Task<PlayerGrowthSnapshot> LoadAsync(string puuid, string mode, CancellationToken token = default, bool refresh = false)
    {
        if (string.IsNullOrWhiteSpace(puuid)) throw new ArgumentException("请先查询玩家");
        var repository = PlayerHistoryRepository.For(matches);
        var batch = await repository.GetBatchAsync(puuid, token, refresh).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var games = batch.Data?.Games?.Games.OrderByDescending(x => x.GameCreation).Take(100).ToArray() ?? [];
        // 为当前模式的完整分析窗口补齐缺字段的摘要，共用全局并发与缓存。
        var enrich = games.Where(x => x.IsCompletedGame() && (mode == "全部" || x.GetModeText() == mode) &&
            (x.GetParticipant(puuid)?.stats is not { HasFarmResult: true, HasDamageResult: true, HasVisionResult: true })).Take(100);
        await Task.WhenAll(enrich.Select(async game =>
        {
            try
            {
                var detail = await repository.GetDetailAsync(game.GameId, token).ConfigureAwait(false);
                int? id = detail?.participantIdentities.FirstOrDefault(p => p.player?.Puuid == puuid)?.participantId;
                var player = id.HasValue ? detail?.participants.FirstOrDefault(p => p.participantId == id) : null;
                if (player?.stats == null) return;
                game.ParticipantIdentities = [new() { ParticipantId = player.participantId, Player = new() { Puuid = puuid } }];
                game.Participants = [player]; if (game.GameDuration <= 0) game.GameDuration = detail!.gameDuration;
            }
            catch (OperationCanceledException) { throw; }
            catch { /* 未能补齐的字段继续显示缺失。 */ }
        })).ConfigureAwait(false);
        var report = PlayerGrowthCalculator.Calculate(games, puuid, mode);
        var training = await ReadTraining(puuid, token).ConfigureAwait(false);
        var inventory = new Dictionary<int, (bool? Owned, long? Points, int? Level)>();
        string inventoryMessage = "收藏品或熟练度未取得；仍可查看已有战绩和本地主练标记";
        // 使用被查询者的身份，不把当前账号的 owned-champions 拼到他人档案上。
        try
        {
            string? raw = allowInventory ? await lcu.GetStringAsync($"/lol-summoner/v2/summoners/puuid/{Uri.EscapeDataString(puuid)}", token).ConfigureAwait(false) : null;
            var profile = string.IsNullOrWhiteSpace(raw) ? null : JObject.Parse(raw);
            long summonerId = profile?.Value<long?>("summonerId") ?? 0;
            if (summonerId > 0 && profile?.Value<string>("puuid") == puuid)
            {
                var ownedTask = lcu.GetStringAsync($"/lol-champions/v1/inventories/{summonerId}/champions", token);
                var masteryTask = lcu.GetStringAsync($"/lol-collections/v1/inventories/{summonerId}/champion-mastery", token);
                await Task.WhenAll(ownedTask, masteryTask).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(ownedTask.Result) && JToken.Parse(ownedTask.Result) is JArray owned)
                    foreach (var c in owned.OfType<JObject>()) inventory[c.Value<int?>("id") ?? 0] = (c.SelectToken("ownership.owned")?.Value<bool?>(), null, null);
                if (!string.IsNullOrWhiteSpace(masteryTask.Result) && JToken.Parse(masteryTask.Result) is JArray mastery)
                    foreach (var c in mastery.OfType<JObject>())
                    { int id = c.Value<int?>("championId") ?? 0; inventory[id] = (inventory.GetValueOrDefault(id).Owned, c.Value<long?>("championPoints"), c.Value<int?>("championLevel")); }
                if (inventory.Count > 0) inventoryMessage = "收藏品按被查询者身份读取；未返回的所有权与熟练度显示为 —";
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { }
        var ids = champions.GetAll().Select(c => c.Id).Concat(report.Champions.Select(c => c.ChampionId)).Concat(training).Concat(inventory.Keys).Where(id => id > 0).Distinct();
        var entries = ids.Select(id =>
        { var data = inventory.GetValueOrDefault(id); return new ChampionArchiveEntry(id, champions.GetDisplayName(id) is { Length: > 0 } name ? name : $"英雄 {id}", data.Owned, data.Points, data.Level, training.Contains(id), report.Champions.FirstOrDefault(c => c.ChampionId == id)); })
            .OrderByDescending(c => c.Training).ThenByDescending(c => c.Recent?.Games ?? 0).ThenByDescending(c => c.MasteryPoints ?? 0).ThenBy(c => c.ChampionId).ToArray();
        return new(puuid, report, entries, games.Select(x => x.GetModeText()).Distinct().OrderBy(x => x).ToArray(), batch.AcquiredAt, batch.IsStale,
            batch.Available ? inventoryMessage : "战绩未取得；不显示伪造的胜率、补刀或伤害。" + inventoryMessage)
            { ReviewNotes = await ReadNotes(puuid, token).ConfigureAwait(false) };
    }
}
