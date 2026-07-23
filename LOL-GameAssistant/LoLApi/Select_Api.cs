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
                        var completed = actionNode["completed"]?.GetValue<bool>() ?? true;

                        // 跳过已完成的动作
                        if (completed || actionId < 0) continue;

                        if (type == "ban" && banChampionIds.Count > 0)
                        {
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
                                // 先选英雄，不锁定（completed=false），等倒计时自然锁定
                                // 这样用户还能在倒计时结束前手动修改
                                await PatchActionAsync(actionId, championId, false);
                                GameMain.infoMsg.AddMsg($"✅ 自动禁用: {ChampionMap.GetChampion(championId)?.RealName ?? championId.ToString()}");
                                banChampionIds.RemoveAt(0);
                            }
                        }
                        else if (type == "pick" && pickChampionIds.Count > 0)
                        {
                            int championId = pickChampionIds[0];
                            // 选择但不锁定（completed=false），让系统倒计时自然锁定
                            await PatchActionAsync(actionId, championId, false);
                            GameMain.infoMsg.AddMsg($"✅ 自动选择: {ChampionMap.GetChampion(championId)?.RealName ?? championId.ToString()}");
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
