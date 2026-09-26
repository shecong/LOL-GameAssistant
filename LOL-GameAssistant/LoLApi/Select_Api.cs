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
        public static async Task<ChampSelectSession?> GetSessionAsync(CancellationToken cancellationToken = default)
        {
            using var client = new HttpClientHelper();
            using Stream? responseStream = await client.GetAsync(
                "/lol-champ-select/v1/session", cancellationToken: cancellationToken).ConfigureAwait(false);
            if (responseStream == null) return null;
            return await responseStream.ReadAsJsonAsync<ChampSelectSession>().ConfigureAwait(false);
        }

        /// <summary>
        /// 执行禁用/选用动作
        /// </summary>
        /// <param name="actionId">动作ID</param>
        /// <param name="championId">英雄ID（0表示取消选择）</param>
        /// <param name="completed">是否完成（通常true）</param>
        public static async Task<bool> PerformActionAsync(
            long actionId, int championId, bool completed = true, CancellationToken cancellationToken = default)
        {
            using var client = new HttpClientHelper();
            var body = JsonConvert.SerializeObject(new { championId, completed });
            using Stream? response = await client.PatchAsync(
                $"/lol-champ-select/v1/session/actions/{actionId}",
                body: body,
                cancellationToken: cancellationToken
            ).ConfigureAwait(false);
            return response != null;
        }

        /// <summary>
        /// 自动禁用英雄：遍历设置中的禁用列表，找到可用的英雄执行禁用
        /// </summary>
        /// <param name="banChampionIds">设置中选定的禁用英雄ID列表</param>
        /// <returns>是否成功执行了禁用动作</returns>
        public static async Task<bool> AutoBanAsync(
            List<int> banChampionIds, CancellationToken cancellationToken = default)
        {
            return await ExecuteAutoActionAsync(banChampionIds, "ban", completed: true, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 自动选用英雄：遍历设置中的选用列表，找到可用的英雄执行选用
        /// </summary>
        /// <param name="pickChampionIds">设置中选定的选用英雄ID列表</param>
        /// <returns>是否成功执行了选用动作</returns>
        public static async Task<bool> AutoPickAsync(
            List<int> pickChampionIds, bool lockIn = true, CancellationToken cancellationToken = default)
        {
            return await ExecuteAutoActionAsync(pickChampionIds, "pick", completed: lockIn, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 执行自动动作（禁用或选用）的核心逻辑
        /// </summary>
        private static async Task<bool> ExecuteAutoActionAsync(
            List<int> desiredChampionIds,
            string actionType,
            bool completed,
            CancellationToken cancellationToken)
        {
            if (desiredChampionIds.Count == 0) return false;

            var session = await GetSessionAsync(cancellationToken).ConfigureAwait(false);
            if (session == null) return false;

            // 收集所有已被禁用/选用的英雄ID（不可用）
            var unavailableIds = CollectUnavailableChampionIds(session);

            // 同一轮可能有多个我方动作；只允许修改本地玩家自己的动作。
            var currentAction = ChampionSelectActionResolver.FindCurrentLocalAction(session, actionType);
            if (currentAction == null) return false;

            int manualIntent = actionType.Equals("pick", StringComparison.OrdinalIgnoreCase)
                ? session.MyTeam.FirstOrDefault(member => member.CellId == session.LocalPlayerCellId)?.ChampionPickIntent ?? 0
                : 0;
            if (manualIntent > 0)
            {
                // championPickIntent 是用户在客户端中已点击但还未锁定的英雄。
                // 只在锁定模式下完成该意图，预选模式不覆盖玩家的当前选择。
                return completed && await PerformActionAsync(
                    currentAction.Id, manualIntent, completed: true, cancellationToken).ConfigureAwait(false);
            }

            // 玩家已经在客户端中点选了英雄时，不再用优先级列表覆盖它。
            // 锁定模式仅完成该玩家已经选择的动作；预选模式则保持原状。
            if (currentAction.ChampionId > 0)
            {
                return completed && await PerformActionAsync(
                    currentAction.Id,
                    currentAction.ChampionId,
                    completed: true,
                    cancellationToken).ConfigureAwait(false);
            }

            var allowedIds = await GetChampionIdSetAsync(
                actionType.Equals("ban", StringComparison.OrdinalIgnoreCase)
                    ? "/lol-champ-select/v1/bannable-champion-ids"
                    : "/lol-champ-select/v1/pickable-champion-ids", cancellationToken).ConfigureAwait(false);
            var disabledIds = await GetChampionIdSetAsync(
                "/lol-champ-select/v1/disabled-champion-ids", cancellationToken).ConfigureAwait(false)
                ?? new HashSet<int>();

            // 在期望列表中找第一个未被禁用/选用的英雄
            int? championToUse = null;
            foreach (var cid in desiredChampionIds)
            {
                if (cid > 0 &&
                    !unavailableIds.Contains(cid) &&
                    !disabledIds.Contains(cid) &&
                    (allowedIds == null || allowedIds.Contains(cid)))
                {
                    championToUse = cid;
                    break;
                }
            }

            // 如果所有期望英雄都被禁用了，选第一个期望英雄作为后备（或跳过）
            if (championToUse == null) return false;

            // 执行动作
            return await PerformActionAsync(
                currentAction.Id,
                championToUse.Value,
                completed,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// LCU 会为不同阶段给出可选/可禁英雄；请求不可用时回退到会话内校验，
        /// 避免因客户端版本差异导致自动化完全失效。
        /// </summary>
        private static async Task<HashSet<int>?> GetChampionIdSetAsync(
            string endpoint, CancellationToken cancellationToken)
        {
            using var client = new HttpClientHelper();
            using Stream? responseStream = await client.GetAsync(
                endpoint, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (responseStream == null)
            {
                return null;
            }

            List<int>? championIds = await responseStream.ReadAsJsonAsync<List<int>>().ConfigureAwait(false);
            return championIds == null ? null : championIds.Where(id => id > 0).ToHashSet();
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