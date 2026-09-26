using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Application.LeagueClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>
/// 外置助手可安全复用的客户端操作集合。
/// 不做 DOM/XHR Hook，也不读取游戏进程内存；所有写入均通过本机 LCU 完成。
/// </summary>
public sealed class LcuClientFeatureService : IClientFeatureService
{
    private const string BackupsDirectoryName = "game-settings-backups";
    private readonly ILcuRequestSender _lcu;
    private readonly SemaphoreSlim _profileSkinCatalogGate = new(1, 1);
    private IReadOnlyDictionary<long, ProfileSkinAsset>? _profileSkinCatalog;
    private DateTimeOffset _profileSkinCatalogLoadedAt;

    private sealed record ProfileSkinAsset(long SkinId, string Name, string ChampionName, string ImagePath, bool IsOwned);

    public LcuClientFeatureService(ILcuRequestSender lcu) => _lcu = lcu;

    public async Task<ClientFeatureResult> DeclineReadyCheckAsync(CancellationToken cancellationToken = default) =>
        await PostResultAsync("/lol-matchmaking/v1/ready-check/decline", "{}", "已拒绝本次对局。", "拒绝对局失败", cancellationToken);

    public async Task<ClientFeatureResult> DodgeChampionSelectAsync(CancellationToken cancellationToken = default)
    {
        bool ok = await _lcu.DeleteAsync("/lol-champ-select/v1/session", cancellationToken).ConfigureAwait(false);
        return ok ? ClientFeatureResult.Success("已请求退出英雄选择。") : ClientFeatureResult.Failure("退出英雄选择失败，请确认当前处于可退出的非自定义选人阶段。");
    }

    public async Task<ClientFeatureResult> SwapAramBenchAsync(int championId, CancellationToken cancellationToken = default)
    {
        if (championId <= 0) return ClientFeatureResult.Failure("英雄 ID 无效。");
        return await PostResultAsync(
            $"/lol-champ-select/v1/session/bench/swap/{championId}", "{}", "已请求换取共享池英雄。", "换英雄失败", cancellationToken);
    }

    public async Task<ClientFeatureResult> CreateQuickLobbyAsync(int queueId, CancellationToken cancellationToken = default)
    {
        if (queueId <= 0) return ClientFeatureResult.Failure("队列 ID 无效。");
        string body = JsonConvert.SerializeObject(new { queueId });
        return await PostResultAsync("/lol-lobby/v2/lobby", body, "已创建目标队列大厅。", "创建大厅失败", cancellationToken);
    }

    public async Task<ClientFeatureResult> ReturnToLobbyAsync(bool startMatchmaking, CancellationToken cancellationToken = default)
    {
        bool playedAgain = await _lcu.PostAsync("/lol-lobby/v2/play-again", "{}", cancellationToken).ConfigureAwait(false);
        if (!playedAgain) return ClientFeatureResult.Failure("返回房间失败，结算可能尚未完成。");
        if (!startMatchmaking) return ClientFeatureResult.Success("已返回房间。");

        // 结算服务刚创建大厅时偶发拒绝匹配；有限重试比无界后台循环更安全。
        for (int attempt = 0; attempt < 8; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            if (await _lcu.PostAsync("/lol-lobby/v2/lobby/matchmaking/search", "{}", cancellationToken).ConfigureAwait(false))
                return ClientFeatureResult.Success("已返回房间并开始匹配。");
        }
        return ClientFeatureResult.Failure("已返回房间，但队伍暂未满足开始匹配条件。");
    }

    public async Task<ClientFeatureResult> HonorRandomEligibleAllyAsync(CancellationToken cancellationToken = default)
    {
        string? json = await _lcu.GetStringAsync("/lol-honor-v2/v1/ballot", cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return ClientFeatureResult.Failure("当前没有可点赞的对局。");

        try
        {
            JObject ballot = JObject.Parse(json);
            long gameId = ballot.Value<long?>("gameId") ?? 0;
            JArray allies = ballot["eligibleAllies"] as JArray ?? new JArray();
            int voteCount = Math.Max(0, ballot.SelectToken("votePool.votes")?.Value<int?>() ?? 1);
            if (gameId <= 0 || allies.Count == 0 || voteCount == 0)
                return ClientFeatureResult.Failure("当前没有可点赞的队友或可用票数。");

            var candidates = allies.OfType<JObject>().OrderBy(_ => Random.Shared.Next()).Take(voteCount).ToArray();
            int honored = 0;
            string[] categories = ["HEART", "COOL", "SHOTCALLER"];
            foreach (JObject ally in candidates)
            {
                string? puuid = ally.Value<string>("puuid");
                long summonerId = ally.Value<long?>("summonerId") ?? 0;
                if (string.IsNullOrWhiteSpace(puuid) || summonerId <= 0) continue;
                string payload = JsonConvert.SerializeObject(new
                {
                    puuid,
                    summonerId,
                    gameId,
                    honorCategory = categories[Random.Shared.Next(categories.Length)]
                });
                if (await _lcu.PostAsync("/lol-honor-v2/v1/honor-player", payload, cancellationToken).ConfigureAwait(false)) honored++;
            }
            return honored > 0
                ? ClientFeatureResult.Success($"已随机点赞 {honored} 位队友。")
                : ClientFeatureResult.Failure("点赞请求未被客户端接受。");
        }
        catch (JsonException)
        {
            return ClientFeatureResult.Failure("荣誉数据格式异常，未执行点赞。");
        }
    }

    public Task<ClientFeatureResult> DownloadReplayAsync(long gameId, CancellationToken cancellationToken = default) =>
        gameId <= 0
            ? Task.FromResult(ClientFeatureResult.Failure("Game ID 无效。"))
            : PostResultAsync($"/lol-replays/v1/rofls/{gameId}/download", "{}", "已开始下载回放。", "下载回放失败", cancellationToken);

    public Task<ClientFeatureResult> WatchReplayAsync(long gameId, CancellationToken cancellationToken = default) =>
        gameId <= 0
            ? Task.FromResult(ClientFeatureResult.Failure("Game ID 无效。"))
            : PostResultAsync($"/lol-replays/v1/rofls/{gameId}/watch", "{}", "已请求启动回放。", "启动回放失败，请先下载完整回放", cancellationToken);

    public async Task<IReadOnlyList<ClientRewardGrant>> GetPendingRewardsAsync(CancellationToken cancellationToken = default)
    {
        string? json = await _lcu.GetStringAsync("/lol-rewards/v1/grants", cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<ClientRewardGrant>();
        try
        {
            return (JArray.Parse(json)).OfType<JObject>()
                .Where(grant => string.Equals(grant.SelectToken("info.status")?.Value<string>(), "PENDING_SELECTION", StringComparison.OrdinalIgnoreCase))
                .Select(ToRewardGrant)
                .Where(grant => !string.IsNullOrWhiteSpace(grant.GrantId))
                .ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<ClientRewardGrant>();
        }
    }

    public async Task<ClientFeatureResult> ClaimAllPendingRewardsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ClientRewardGrant> pending = await GetPendingRewardsAsync(cancellationToken).ConfigureAwait(false);
        if (pending.Count == 0) return ClientFeatureResult.Failure("没有待领取的选择奖励。");

        int claimed = 0;
        foreach (ClientRewardGrant grant in pending)
        {
            // 奖励详情中的 rewardGroupId 不会出现在简化模型中，重新读取原 grant 以避免猜测端点参数。
            string? json = await _lcu.GetStringAsync("/lol-rewards/v1/grants", cancellationToken).ConfigureAwait(false);
            JObject? raw = TryFindGrant(json, grant.GrantId);
            string? groupId = raw?.SelectToken("rewardGroup.id")?.Value<string>();
            if (string.IsNullOrWhiteSpace(groupId) || grant.RewardIds.Count == 0) continue;
            int maximum = raw?.SelectToken("rewardGroup.selectionStrategyConfig.maxSelectionsAllowed")?.Value<int?>()
                ?? grant.AvailableChoices;
            int selectionCount = Math.Clamp(maximum, 1, grant.RewardIds.Count);
            string payload = JsonConvert.SerializeObject(new
            {
                grantId = grant.GrantId,
                rewardGroupId = groupId,
                selections = grant.RewardIds.Take(selectionCount).ToArray()
            });
            if (await _lcu.PostAsync($"/lol-rewards/v1/grants/{grant.GrantId}/select", payload, cancellationToken).ConfigureAwait(false)) claimed++;
        }
        return claimed > 0
            ? ClientFeatureResult.Success($"已提交 {claimed} 组奖励领取请求。")
            : ClientFeatureResult.Failure("领取奖励失败，可能需要在客户端手动选择。 ");
    }

    public async Task<IReadOnlyList<ClientSettingsBackup>> ListGameSettingsBackupsAsync(CancellationToken cancellationToken = default)
    {
        string directory = await GetAccountBackupDirectoryAsync(cancellationToken).ConfigureAwait(false);
        if (!Directory.Exists(directory)) return Array.Empty<ClientSettingsBackup>();
        return Directory.EnumerateFiles(directory, "*.json")
            .Select(path => new FileInfo(path))
            .Select(file => new ClientSettingsBackup(Path.GetFileNameWithoutExtension(file.Name), file.CreationTimeUtc))
            .OrderByDescending(backup => backup.CreatedAt)
            .ToArray();
    }

    public async Task<ClientFeatureResult> SaveGameSettingsBackupAsync(string name, CancellationToken cancellationToken = default)
    {
        string safeName = NormalizeBackupName(name);
        if (safeName.Length == 0) return ClientFeatureResult.Failure("备份名称只能包含文字、数字、空格、- 和 _。 ");
        string? settings = await _lcu.GetStringAsync("/lol-game-settings/v1/game-settings", cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(settings)) return ClientFeatureResult.Failure("未读取到客户端游戏设置。");
        string directory = await GetAccountBackupDirectoryAsync(cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, safeName + ".json");
        await File.WriteAllTextAsync(path, settings, cancellationToken).ConfigureAwait(false);
        return ClientFeatureResult.Success($"已保存设置备份“{safeName}”。");
    }

    public async Task<ClientFeatureResult> RestoreGameSettingsBackupAsync(string name, CancellationToken cancellationToken = default)
    {
        string safeName = NormalizeBackupName(name);
        string directory = await GetAccountBackupDirectoryAsync(cancellationToken).ConfigureAwait(false);
        string path = Path.Combine(directory, safeName + ".json");
        if (!File.Exists(path)) return ClientFeatureResult.Failure("未找到该设置备份。");
        string settings = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        bool ok = await _lcu.PutAsync("/lol-game-settings/v1/game-settings", settings, cancellationToken).ConfigureAwait(false);
        return ok ? ClientFeatureResult.Success($"已恢复设置备份“{safeName}”。") : ClientFeatureResult.Failure("恢复设置失败。");
    }

    public async Task<ClientFeatureResult> DeleteGameSettingsBackupAsync(string name, CancellationToken cancellationToken = default)
    {
        string safeName = NormalizeBackupName(name);
        string directory = await GetAccountBackupDirectoryAsync(cancellationToken).ConfigureAwait(false);
        string path = Path.Combine(directory, safeName + ".json");
        if (!File.Exists(path)) return ClientFeatureResult.Failure("未找到该设置备份。");
        File.Delete(path);
        return ClientFeatureResult.Success($"已删除设置备份“{safeName}”。");
    }

    public async Task<IReadOnlyList<FriendActivity>> GetFriendActivitiesAsync(CancellationToken cancellationToken = default)
    {
        string? json = await _lcu.GetStringAsync("/lol-chat/v1/friends", cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<FriendActivity>();
        try
        {
            return JArray.Parse(json).OfType<JObject>().Select(friend =>
            {
                JObject? lol = friend["lol"] as JObject;
                long timestamp = lol?.Value<long?>("timeStamp") ?? 0;
                DateTimeOffset? startedAt = timestamp > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
                    : null;
                string displayName = friend.Value<string>("gameName") ?? friend.Value<string>("name") ?? "未知玩家";
                string gameStatus = lol?.Value<string>("gameStatus") ?? "outOfGame";
                int queueId = lol?.Value<int?>("queueId") ?? 0;
                string queueName = lol?.Value<string>("gameQueueType") ?? lol?.Value<string>("gameMode") ?? "";
                return new FriendActivity(
                    friend.Value<string>("puuid") ?? "",
                    displayName,
                    friend.Value<string>("availability") ?? "offline",
                    gameStatus,
                    queueId,
                    queueName,
                    startedAt);
            }).Where(friend => !string.IsNullOrWhiteSpace(friend.Puuid)).ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<FriendActivity>();
        }
    }

    public async Task<ClientFeatureResult> UpdateChatPresenceAsync(string availability, string statusMessage, CancellationToken cancellationToken = default)
    {
        string normalizedAvailability = availability.Trim().ToLowerInvariant();
        if (normalizedAvailability is not ("chat" or "away" or "dnd" or "offline" or "mobile"))
            return ClientFeatureResult.Failure("不支持的在线状态。");
        string? json = await _lcu.GetStringAsync("/lol-chat/v1/me", cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return ClientFeatureResult.Failure("未读取到聊天状态。");
        try
        {
            JObject me = JObject.Parse(json);
            me["availability"] = normalizedAvailability;
            me["statusMessage"] = statusMessage.Trim()[..Math.Min(128, statusMessage.Trim().Length)];
            bool ok = await _lcu.PutAsync("/lol-chat/v1/me", me.ToString(Formatting.None), cancellationToken).ConfigureAwait(false);
            return ok ? ClientFeatureResult.Success("在线状态和签名已更新。") : ClientFeatureResult.Failure("更新在线状态失败。");
        }
        catch (JsonException)
        {
            return ClientFeatureResult.Failure("聊天状态格式异常。");
        }
    }

    public async Task<IReadOnlyList<ClientSkinChoice>> GetProfileBackgroundChoicesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<long, ProfileSkinAsset> catalog = await GetProfileSkinCatalogAsync(cancellationToken).ConfigureAwait(false);
        return catalog.Values
            .OrderBy(item => item.ChampionName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => new ClientSkinChoice(item.SkinId, item.Name, item.ChampionName, item.IsOwned))
            .ToArray();
    }

    public async Task<ClientSkinPreview?> GetProfileSkinPreviewAsync(long skinId, CancellationToken cancellationToken = default)
    {
        if (skinId <= 0) return null;

        IReadOnlyDictionary<long, ProfileSkinAsset> catalog = await GetProfileSkinCatalogAsync(cancellationToken).ConfigureAwait(false);
        if (!catalog.TryGetValue(skinId, out ProfileSkinAsset? skin)) return null;
        byte[]? image = string.IsNullOrWhiteSpace(skin.ImagePath)
            ? null
            : await _lcu.GetBytesAsync(skin.ImagePath, cancellationToken).ConfigureAwait(false);
        return new ClientSkinPreview(skin.SkinId, skin.Name, skin.ChampionName, image);
    }

    private async Task<IReadOnlyDictionary<long, ProfileSkinAsset>> GetProfileSkinCatalogAsync(CancellationToken cancellationToken)
    {
        if (_profileSkinCatalog != null && DateTimeOffset.UtcNow - _profileSkinCatalogLoadedAt < TimeSpan.FromMinutes(15))
            return _profileSkinCatalog;

        await _profileSkinCatalogGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_profileSkinCatalog != null && DateTimeOffset.UtcNow - _profileSkinCatalogLoadedAt < TimeSpan.FromMinutes(15))
                return _profileSkinCatalog;

            // The inventory can omit unowned skins and some clients do not include image paths.
            // The local game-data catalog is the source of previewable choices.
            string? skinsJson = await _lcu.GetStringAsync("/lol-game-data/assets/v1/skins.json", cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(skinsJson)) return new Dictionary<long, ProfileSkinAsset>();
            JToken skinsRoot = JToken.Parse(skinsJson);
            IEnumerable<JObject> skins = skinsRoot switch
            {
                JObject map => map.Properties().Select(property => property.Value).OfType<JObject>(),
                JArray array => array.OfType<JObject>(),
                _ => Enumerable.Empty<JObject>()
            };
            var championNames = new Dictionary<long, string>();
            string? championsJson = await _lcu.GetStringAsync("/lol-game-data/assets/v1/champion-summary.json", cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(championsJson))
            {
                foreach (JObject champion in JArray.Parse(championsJson).OfType<JObject>())
                {
                    long id = champion.Value<long?>("id") ?? 0;
                    if (id > 0) championNames[id] = champion.Value<string>("name") ?? $"英雄 {id}";
                }
            }
            var ownedIds = new HashSet<long>();
            string? summonerJson = await _lcu.GetStringAsync("/lol-summoner/v1/current-summoner", cancellationToken).ConfigureAwait(false);
            long summonerId = string.IsNullOrWhiteSpace(summonerJson) ? 0 : JObject.Parse(summonerJson).Value<long?>("summonerId") ?? 0;
            if (summonerId > 0)
            {
                string? inventoryJson = await _lcu.GetStringAsync($"/lol-champions/v1/inventories/{summonerId}/champions", cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(inventoryJson))
                {
                    foreach (JObject champion in JArray.Parse(inventoryJson).OfType<JObject>())
                        foreach (JObject ownedSkin in champion["skins"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                        {
                            if (ownedSkin.SelectToken("ownership.owned")?.Value<bool?>() == true)
                                ownedIds.Add(ownedSkin.Value<long?>("id") ?? 0);
                        }
                }
            }
            var catalog = new Dictionary<long, ProfileSkinAsset>();
            foreach (JObject skin in skins)
            {
                long skinId = skin.Value<long?>("id") ?? 0;
                if (skinId <= 0 || catalog.ContainsKey(skinId)) continue;
                string imagePath = skin.Value<string>("uncenteredSplashPath")
                    ?? skin.Value<string>("splashPath")
                    ?? skin.Value<string>("tilePath")
                    ?? "";
                if (string.IsNullOrWhiteSpace(imagePath)) continue;
                long championId = skinId / 1000;
                catalog[skinId] = new ProfileSkinAsset(
                    skinId,
                    skin.Value<string>("name") ?? $"皮肤 {skinId}",
                    championNames.GetValueOrDefault(championId, $"英雄 {championId}"),
                    imagePath,
                    ownedIds.Contains(skinId));
            }
            _profileSkinCatalog = catalog;
            _profileSkinCatalogLoadedAt = DateTimeOffset.UtcNow;
            return catalog;
        }
        catch (JsonException)
        {
            return new Dictionary<long, ProfileSkinAsset>();
        }
        finally
        {
            _profileSkinCatalogGate.Release();
        }
    }

    public Task<ClientFeatureResult> UpdateProfileBackgroundAsync(long skinId, CancellationToken cancellationToken = default) =>
        skinId <= 0
            ? Task.FromResult(ClientFeatureResult.Failure("皮肤 ID 无效。"))
            : PostResultAsync("/lol-summoner/v1/current-summoner/summoner-profile", JsonConvert.SerializeObject(new { key = "backgroundSkinId", value = skinId }), "已更新生涯背景。", "更新生涯背景失败", cancellationToken);

    public Task<ClientFeatureResult> SetProfileIconAsync(int profileIconId, CancellationToken cancellationToken = default) =>
        profileIconId <= 0
            ? Task.FromResult(ClientFeatureResult.Failure("头像 ID 无效。"))
            : PutResultAsync("/lol-summoner/v1/current-summoner/icon", profileIconId.ToString(CultureInfo.InvariantCulture), "已更新召唤师头像。", "更新头像失败", cancellationToken);

    public Task<ClientFeatureResult> ClearChallengeBadgesAsync(CancellationToken cancellationToken = default) =>
        PostResultAsync("/lol-challenges/v1/update-player-preferences", JsonConvert.SerializeObject(new { challengeIds = Array.Empty<long>() }), "已清空身份徽章。", "清空身份徽章失败", cancellationToken);

    private async Task<ClientFeatureResult> PostResultAsync(string endpoint, string body, string success, string failed, CancellationToken cancellationToken) =>
        await _lcu.PostAsync(endpoint, body, cancellationToken).ConfigureAwait(false)
            ? ClientFeatureResult.Success(success)
            : ClientFeatureResult.Failure(failed);

    private async Task<ClientFeatureResult> PutResultAsync(string endpoint, string body, string success, string failed, CancellationToken cancellationToken) =>
        await _lcu.PutAsync(endpoint, body, cancellationToken).ConfigureAwait(false)
            ? ClientFeatureResult.Success(success)
            : ClientFeatureResult.Failure(failed);

    private static ClientRewardGrant ToRewardGrant(JObject grant)
    {
        JObject? group = grant["rewardGroup"] as JObject;
        string title = grant.SelectToken("localizations.title")?.Value<string>()
            ?? grant.SelectToken("info.title")?.Value<string>()
            ?? "未命名奖励";
        int choices = group?.SelectToken("selectionStrategyConfig.maxSelectionsAllowed")?.Value<int?>()
            ?? group?.Value<int?>("selectionLimit")
            ?? group?.Value<int?>("maximumSelections")
            ?? 1;
        IReadOnlyList<string> ids = (group?["rewards"] as JArray ?? new JArray()).OfType<JObject>()
            .Select(reward => reward.Value<string>("id"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>().ToArray();
        return new ClientRewardGrant(grant.SelectToken("info.id")?.Value<string>() ?? "", title, choices, ids);
    }

    private static JObject? TryFindGrant(string? json, string grantId)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JArray.Parse(json).OfType<JObject>().FirstOrDefault(item => string.Equals(item.SelectToken("info.id")?.Value<string>(), grantId, StringComparison.Ordinal));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<string> GetAccountBackupDirectoryAsync(CancellationToken cancellationToken)
    {
        string account = "default";
        string? json = await _lcu.GetStringAsync("/lol-summoner/v1/current-summoner", cancellationToken).ConfigureAwait(false);
        try
        {
            JObject? me = string.IsNullOrWhiteSpace(json) ? null : JObject.Parse(json);
            account = me?.Value<string>("puuid") ?? me?.Value<string>("summonerId") ?? account;
        }
        catch (JsonException) { }
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, BackupsDirectoryName, NormalizeBackupName(account));
    }

    private static string NormalizeBackupName(string? value)
    {
        string trimmed = (value ?? "").Trim();
        if (trimmed.Length == 0) return "";
        string safe = string.Concat(trimmed.Select(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '_' ? ch : '_')).Trim();
        return safe[..Math.Min(80, safe.Length)];
    }
}