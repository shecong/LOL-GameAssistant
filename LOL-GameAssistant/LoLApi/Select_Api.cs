using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Helper;
using Newtonsoft.Json;
using System.Text.Json.Nodes;

namespace LOL_GameAssistant.LoLApi
{
    /// <summary>
    /// 英雄选择阶段 API
    /// </summary>
    public static class Select_Api
    {
        /// <summary>
        /// 获取英雄选择阶段会话完整数据
        /// </summary>
        public static async Task<JsonNode?> GetSessionAsync()
        {
            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync($"/lol-champ-select/v1/session");
            if (responseStream == null) return null;
            var json = await responseStream.ReadAsStringJsonAsync();
            if (string.IsNullOrEmpty(json)) return null;
            return JsonNode.Parse(json);
        }

        /// <summary>
        /// 执行英雄选择动作（ban 或 pick）
        /// </summary>
        /// <param name="actionId">动作ID（来自 session.actions）</param>
        /// <param name="championId">英雄ID</param>
        /// <param name="completed">是否完成（true=锁定）</param>
        public static async Task<bool> PatchActionAsync(int actionId, int championId, bool completed = true)
        {
            try
            {
                var body = JsonConvert.SerializeObject(new
                {
                    championId = championId,
                    completed = completed
                });
                HttpClentHelper client = new HttpClentHelper();
                Stream? responseStream = await client.PatchAsync(
                    $"/lol-champ-select/v1/session/actions/{actionId}",
                    body: body);
                return responseStream != null;
            }
            catch (Exception ex)
            {
                GameMain.infoMsg.AddMsg($"选择英雄失败(actionId={actionId}, championId={championId}): {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 根据当前会话，执行自动禁用和自动选择逻辑
        /// </summary>
        /// <param name="session">英雄选择会话 JSON</param>
        /// <param name="banChampionIds">要禁用的英雄ID列表</param>
        /// <param name="pickChampionId">要选择的英雄ID（取第一个有效的）</param>
        public static async Task ExecuteAutoActionsAsync(
            JsonNode session,
            List<int> banChampionIds,
            List<int> pickChampionIds)
        {
            try
            {
                var actions = session["actions"]?.AsArray();
                if (actions == null || actions.Count == 0) return;

                // 获取当前玩家的 cellId
                var localCellId = session["localPlayerCellId"]?.GetValue<int>() ?? -1;
                if (localCellId < 0) return;

                // 遍历所有 action 子数组
                foreach (var actionGroup in actions)
                {
                    var group = actionGroup?.AsArray();
                    if (group == null) continue;

                    foreach (var actionNode in group)
                    {
                        if (actionNode == null) continue;

                        var actorCellId = actionNode["actorCellId"]?.GetValue<int>() ?? -1;
                        if (actorCellId != localCellId) continue;

                        var type = actionNode["type"]?.GetValue<string>() ?? "";
                        var actionId = actionNode["id"]?.GetValue<int>() ?? -1;
                        var isInProgress = actionNode["isInProgress"]?.GetValue<bool>() ?? false;
                        var completed = actionNode["completed"]?.GetValue<bool>() ?? true;

                        // 跳过已完成或正在进行的动作
                        if (completed || isInProgress || actionId < 0) continue;

                        if (type == "ban" && banChampionIds.Count > 0)
                        {
                            // 禁用英雄：选择一个未完成的 ban action
                            int championId = banChampionIds[0];
                            // 检查这个英雄是否已经被 ban
                            var bans = session["bans"]?["myTeamBans"]?.AsArray();
                            bool alreadyBanned = false;
                            if (bans != null)
                            {
                                foreach (var b in bans)
                                {
                                    if (b?.GetValue<int>() == championId) { alreadyBanned = true; break; }
                                }
                            }
                            if (!alreadyBanned)
                            {
                                await PatchActionAsync(actionId, championId, true);
                                GameMain.infoMsg.AddMsg($"自动禁用: {ChampionMap.GetChampion(championId)?.RealName ?? championId.ToString()}");
                                banChampionIds.RemoveAt(0); // 消费掉这个 ban 目标
                            }
                        }
                        else if (type == "pick" && pickChampionIds.Count > 0)
                        {
                            // 选择英雄：选择一个未完成的 pick action
                            int championId = pickChampionIds[0];
                            await PatchActionAsync(actionId, championId, true);
                            GameMain.infoMsg.AddMsg($"自动选择: {ChampionMap.GetChampion(championId)?.RealName ?? championId.ToString()}");
                            // 选择后不再移除，因为可能有多轮 pick
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GameMain.infoMsg.AddMsg($"执行自动ban/pick异常: {ex.Message}");
            }
        }
    }
}
