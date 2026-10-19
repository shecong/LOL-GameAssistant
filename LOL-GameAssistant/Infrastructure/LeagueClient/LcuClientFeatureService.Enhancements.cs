using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Application.LeagueClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

public sealed partial class LcuClientFeatureService
{
    private readonly SemaphoreSlim _settingsGate = new(1, 1);
    private readonly SemaphoreSlim _rewardGate = new(1, 1);
    public async Task<ClientReplayState> GetReplayStateAsync(long gameId, CancellationToken cancellationToken = default)
    {
        if (gameId <= 0) return new(gameId, ReplayAvailability.Unavailable, null, "无效对局 ID");
        string? json = await _lcu.GetStringAsync($"/lol-replays/v1/metadata/{gameId}", cancellationToken).ConfigureAwait(false);
        return ParseReplay(gameId, json);
    }
    internal static ClientReplayState ParseReplay(long gameId, string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new(gameId, ReplayAvailability.Unknown, null, "未取得回放元数据，无法确认下载状态");
        try
        {
            JObject data = JObject.Parse(json); string state = (data.Value<string>("state") ?? "").ToLowerInvariant();
            var availability = state switch
            {
                "watch" or "ready" or "downloaded" => ReplayAvailability.Ready,
                "downloading" => ReplayAvailability.Downloading,
                "download" or "available" => ReplayAvailability.Downloadable,
                "expired" or "incompatible" or "unavailable" or "failed" => ReplayAvailability.Unavailable,
                _ => ReplayAvailability.Unknown
            };
            double? progress = data.Value<double?>("downloadProgress");
            if (progress is < 0 or > 100 || progress.HasValue && !double.IsFinite(progress.Value)) progress = null;
            // 不猜测字段是 0..1 还是 0..100；只在已明确的百分比字段存在时展示。
            double? percentage = data.Value<double?>("downloadProgressPercent");
            if (percentage is < 0 or > 100 || percentage.HasValue && !double.IsFinite(percentage.Value)) percentage = null;
            string message = availability switch { ReplayAvailability.Ready => "已下载，可观看", ReplayAvailability.Downloading => "正在下载" + (percentage.HasValue ? $" {percentage:0.#}%" : "（客户端未提供明确百分比）"), ReplayAvailability.Downloadable => "可请求下载", ReplayAvailability.Unavailable => "回放不可用：" + state, _ => "客户端状态待核实：" + (state.Length == 0 ? "未提供" : state) };
            return new(gameId, availability, percentage, message);
        }
        catch (JsonException) { return new(gameId, ReplayAvailability.Unknown, null, "回放元数据格式变化，未假定完成"); }
    }
    public async Task<ClientFeatureResult> ClaimRewardAsync(string grantId, IReadOnlyList<string> selections, CancellationToken cancellationToken = default)
    {
        await _rewardGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            JObject? grant = TryFindGrant(await _lcu.GetStringAsync("/lol-rewards/v1/grants", cancellationToken).ConfigureAwait(false), grantId);
            if (grant?.SelectToken("info.status")?.Value<string>() != "PENDING_SELECTION") return ClientFeatureResult.Failure("奖励状态已变化，请刷新后重新选择");
            var parsed = ToRewardGrant(grant); var selected = selections.Distinct(StringComparer.Ordinal).ToArray();
            if (selected.Length != selections.Count || selected.Length < parsed.MinimumSelections || selected.Length > parsed.AvailableChoices || selected.Any(x => !parsed.RewardIds.Contains(x)))
                return ClientFeatureResult.Failure($"请从当前奖励中选择 {parsed.MinimumSelections}～{parsed.AvailableChoices} 项，不能重复选择");
            string? groupId = grant.SelectToken("rewardGroup.id")?.Value<string>();
            if (string.IsNullOrWhiteSpace(groupId)) return ClientFeatureResult.Failure("奖励缺少分组标识，未提交");
            bool ok = await _lcu.PostAsync($"/lol-rewards/v1/grants/{Uri.EscapeDataString(grantId)}/select",
                JsonConvert.SerializeObject(new { grantId, rewardGroupId = groupId, selections = selected }), cancellationToken).ConfigureAwait(false);
            if (!ok) return ClientFeatureResult.Failure("客户端未接受领取，请刷新当前奖励");
            JObject? latest = TryFindGrant(await _lcu.GetStringAsync("/lol-rewards/v1/grants", cancellationToken).ConfigureAwait(false), grantId);
            return latest == null || latest.SelectToken("info.status")?.Value<string>() == "PENDING_SELECTION"
                ? ClientFeatureResult.Success("领取请求已提交，最终状态暂未确认，请刷新核对") : ClientFeatureResult.Success("已核对奖励状态更新");
        }
        finally { _rewardGate.Release(); }
    }
    private async Task<JObject?> ReadSettings(string category, CancellationToken token)
    {
        string? json = await _lcu.GetStringAsync($"/lol-game-settings/v1/{category}", token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JObject.Parse(json); } catch (JsonException) { return null; }
    }
    private async Task<JObject?> ReadBackup(string name, CancellationToken token)
    {
        string file = Path.Combine(await GetAccountBackupDirectoryAsync(token).ConfigureAwait(false), NormalizeBackupName(name) + ".json");
        if (!File.Exists(file)) return null;
        JObject data = JObject.Parse(await File.ReadAllTextAsync(file, token).ConfigureAwait(false));
        if (data.Value<int?>("formatVersion") is { } version && version != 2) throw new InvalidOperationException("该备份结构版本不受支持");
        return data["formatVersion"] == null ? new JObject { ["formatVersion"] = 1, ["gameSettings"] = data } : data;
    }
    private static IEnumerable<ClientBackupDifference> Differences(string category, JObject? current, JObject? saved)
    {
        if (saved == null) yield break;
        foreach (var property in saved.Properties()) if (!JToken.DeepEquals(current?[property.Name], property.Value))
            yield return new(category, property.Name, current?[property.Name]?.ToString(Formatting.None) ?? "—", property.Value.ToString(Formatting.None));
    }
    public async Task<ClientBackupPreview> PreviewBackupAsync(string name, CancellationToken cancellationToken = default)
    {
        var saved = await ReadBackup(name, cancellationToken).ConfigureAwait(false);
        if (saved == null) return new(name, false, false, [], "未找到当前账号的备份");
        var game = await ReadSettings("game-settings", cancellationToken).ConfigureAwait(false);
        var input = await ReadSettings("input-settings", cancellationToken).ConfigureAwait(false);
        var changes = Differences("游戏设置", game, saved["gameSettings"] as JObject).Concat(Differences("按键设置", input, saved["inputSettings"] as JObject)).ToArray();
        return new(name, saved["gameSettings"] is JObject, saved["inputSettings"] is JObject, changes,
            $"备份版本 {saved["formatVersion"]} · 客户端 {saved["clientVersion"] ?? "未记录"} · {changes.Length} 个顶层设置组有差异；仅展示并恢复当前账号目录中的备份");
    }
    private static bool ContainsSettings(JToken? actual, JToken expected)
    {
        if (expected is JObject obj) return actual is JObject target && obj.Properties().All(p => target[p.Name] is { } value && ContainsSettings(value, p.Value));
        return JToken.DeepEquals(actual, expected);
    }
    private async Task<bool> WriteAndVerify(string category, JObject data, CancellationToken token)
    {
        string endpoint = $"/lol-game-settings/v1/{category}";
        var put = await _capabilities.CheckAsync("PUT", endpoint, token).ConfigureAwait(false);
        bool ok = put.State == ClientCapabilityState.MethodUnavailable
            ? (await _capabilities.CheckAsync("PATCH", endpoint, token).ConfigureAwait(false)).State is ClientCapabilityState.Advertised or ClientCapabilityState.Observed && await _lcu.PatchAsync(endpoint, data.ToString(Formatting.None), token).ConfigureAwait(false)
            : await _lcu.PutAsync(endpoint, data.ToString(Formatting.None), token).ConfigureAwait(false);
        if (!ok) return false;
        var current = await ReadSettings(category, token).ConfigureAwait(false); return current != null && ContainsSettings(current, data);
    }
    public async Task<ClientFeatureResult> RestoreBackupSelectionAsync(string name, bool gameSettings, bool inputSettings, CancellationToken cancellationToken = default)
    {
        if (!gameSettings && !inputSettings) return ClientFeatureResult.Failure("请至少选择一类设置");
        await _settingsGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var backup = await ReadBackup(name, cancellationToken).ConfigureAwait(false);
            if (backup == null) return ClientFeatureResult.Failure("未找到该备份");
            if (gameSettings && backup["gameSettings"] is not JObject || inputSettings && backup["inputSettings"] is not JObject) return ClientFeatureResult.Failure("备份不含所选类别，请取消没有记录的类别");
            var previousGame = gameSettings ? await ReadSettings("game-settings", cancellationToken).ConfigureAwait(false) : null;
            var previousInput = inputSettings ? await ReadSettings("input-settings", cancellationToken).ConfigureAwait(false) : null;
            if (gameSettings && previousGame == null || inputSettings && previousInput == null) return ClientFeatureResult.Failure("无法读取恢复前状态，未写入任何设置");
            string folder = await GetAccountBackupDirectoryAsync(cancellationToken).ConfigureAwait(false); Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "恢复前_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_ffff") + ".json"),
                new JObject { ["formatVersion"] = 2, ["createdAt"] = DateTimeOffset.UtcNow, ["gameSettings"] = previousGame, ["inputSettings"] = previousInput }.ToString(), cancellationToken).ConfigureAwait(false);
            var attempted = new List<(string Category, JObject Old)>();
            try
            {
                if (gameSettings) { attempted.Add(("game-settings", previousGame!)); if (!await WriteAndVerify("game-settings", (JObject)backup["gameSettings"]!, cancellationToken).ConfigureAwait(false)) throw new InvalidOperationException("游戏设置未通过读取核验"); }
                if (inputSettings) { attempted.Add(("input-settings", previousInput!)); if (!await WriteAndVerify("input-settings", (JObject)backup["inputSettings"]!, cancellationToken).ConfigureAwait(false)) throw new InvalidOperationException("按键设置未通过读取核验"); }
                return ClientFeatureResult.Success("所选设置已恢复并通过读取核验；恢复前快照已保留");
            }
            catch (Exception e)
            {
                using var rollback = new CancellationTokenSource(TimeSpan.FromSeconds(15)); bool restored = true;
                foreach (var item in attempted.AsEnumerable().Reverse())
                    try { restored &= await WriteAndVerify(item.Category, item.Old, rollback.Token).ConfigureAwait(false); } catch { restored = false; }
                if (e is OperationCanceledException && cancellationToken.IsCancellationRequested && restored) throw;
                return ClientFeatureResult.Failure(e.Message + (restored ? "；已核验恢复原状态" : "；部分原状态恢复未确认，请用恢复前快照检查"));
            }
        }
        finally { _settingsGate.Release(); }
    }
}
