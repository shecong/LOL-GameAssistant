using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Helper;
using Newtonsoft.Json;

namespace LOL_GameAssistant.LoLApi
{
    /// <summary>
    /// 英雄选择阶段 API（自动禁用/选用英雄）
    /// </summary>
    public static class Select_Api
    {
        /// <summary>
        /// 获取当前选人会话
        /// </summary>
        public static async Task<ChampSelectSession?> GetSessionAsync()
        {
            using var client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync("/lol-champ-select/v1/session");
            if (responseStream == null) return null;
            return await responseStream.ReadAsJsonAsync<ChampSelectSession>();
        }

        /// <summary>
        /// 执行禁用/选用动作
        /// </summary>
        /// <param name="actionId">动作ID</param>
        /// <param name="championId">英雄ID（0表示取消选择）</param>
        /// <param name="completed">是否完成（通常true）</param>
        public static async Task<bool> PerformActionAsync(long actionId, int championId, bool completed = true)
        {
            using var client = new HttpClentHelper();
            var body = JsonConvert.SerializeObject(new { championId, completed });
            Stream? response = await client.PatchAsync(
                $"/lol-champ-select/v1/session/actions/{actionId}",
                body: body
            );
            return response != null;
        }

        /// <summary>
        /// 自动禁用英雄：遍历设置中的禁用列表，找到可用的英雄执行禁用
        /// </summary>
        /// <param name="banChampionIds">设置中选定的禁用英雄ID列表</param>
        /// <returns>是否成功执行了禁用动作</returns>
        public static async Task<bool> AutoBanAsync(List<int> banChampionIds)
        {
            return await ExecuteAutoActionAsync(banChampionIds, "ban");
        }

        /// <summary>
        /// 自动选用英雄：遍历设置中的选用列表，找到可用的英雄执行选用
        /// </summary>
        /// <param name="pickChampionIds">设置中选定的选用英雄ID列表</param>
        /// <returns>是否成功执行了选用动作</returns>
        public static async Task<bool> AutoPickAsync(List<int> pickChampionIds)
        {
            return await ExecuteAutoActionAsync(pickChampionIds, "pick");
        }

        /// <summary>
        /// 执行自动动作（禁用或选用）的核心逻辑
        /// </summary>
        private static async Task<bool> ExecuteAutoActionAsync(List<int> desiredChampionIds, string actionType)
        {
            if (desiredChampionIds.Count == 0) return false;

            var session = await GetSessionAsync();
            if (session == null) return false;

            // 收集所有已被禁用/选用的英雄ID（不可用）
            var unavailableIds = CollectUnavailableChampionIds(session);

            // 找到当前轮到我方、未完成的指定类型动作
            var currentAction = FindCurrentAllyAction(session, actionType);
            if (currentAction == null) return false;

            // 在期望列表中找第一个未被禁用/选用的英雄
            int? championToUse = null;
            foreach (var cid in desiredChampionIds)
            {
                if (!unavailableIds.Contains(cid))
                {
                    championToUse = cid;
                    break;
                }
            }

            // 如果所有期望英雄都被禁用了，选第一个期望英雄作为后备（或跳过）
            if (championToUse == null) return false;

            // 执行动作
            return await PerformActionAsync(currentAction.Id, championToUse.Value);
        }

        /// <summary>
        /// 收集所有不可用的英雄ID（已被禁用、已被选用、或已被队友/对手锁定）
        /// </summary>
        private static HashSet<int> CollectUnavailableChampionIds(ChampSelectSession session)
        {
            var unavailable = new HashSet<int>();

            // 遍历所有动作，收集已完成的禁用/选用
            foreach (var round in session.Actions)
            {
                foreach (var action in round)
                {
                    if (action.Completed && action.ChampionId > 0)
                    {
                        unavailable.Add(action.ChampionId);
                    }
                }
            }

            // 收集队伍中已选的英雄
            foreach (var member in session.MyTeam)
            {
                if (member.ChampionId > 0)
                    unavailable.Add(member.ChampionId);
            }
            foreach (var member in session.TheirTeam)
            {
                if (member.ChampionId > 0)
                    unavailable.Add(member.ChampionId);
            }

            return unavailable;
        }

        /// <summary>
        /// 找到当前轮到我方进行的指定类型动作
        /// </summary>
        private static ChampSelectAction? FindCurrentAllyAction(ChampSelectSession session, string actionType)
        {
            foreach (var round in session.Actions)
            {
                foreach (var action in round)
                {
                    if (action.IsInProgress &&
                        !action.Completed &&
                        action.IsAllyAction &&
                        string.Equals(action.Type, actionType, StringComparison.OrdinalIgnoreCase))
                    {
                        return action;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// 通过英雄名获取英雄ID
        /// </summary>
        public static int? GetChampionIdByName(string championName)
        {
            var map = Helper.ChampionMap.GetChampionMap();
            foreach (var entry in map)
            {
                if (string.Equals(entry.Value.RealName, championName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(entry.Value.Label, championName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(entry.Value.Nickname, championName, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Key;
                }
            }
            return null;
        }
    }
}
