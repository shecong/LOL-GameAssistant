using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.LoLApi
{
    /// <summary>
    /// Game_Api 对局流程部分：大厅、匹配、接受、游戏状态与实时队伍。
    /// </summary>
    public static partial class Game_Api
    {
        /// <summary>
        /// 开始自动匹配对局。
        /// </summary>
        public static async Task OpenGameServer(CancellationToken cancellationToken = default)
        {
            using var client = new HttpClientHelper();
            LcuHttpResult result = await client.SendRequestResultAsync("POST", "/lol-lobby/v2/lobby/matchmaking/search", cancellationToken: cancellationToken).ConfigureAwait(false);
            EnsureLobbyCommandSucceeded(result);
        }

        /// <summary>
        /// 接受当前匹配确认。
        /// </summary>
        public static async Task GameTrueServer(CancellationToken cancellationToken = default)
        {
            using var client = new HttpClientHelper();
            LcuHttpResult result = await client.SendRequestResultAsync("POST", "/lol-matchmaking/v1/ready-check/accept", cancellationToken: cancellationToken).ConfigureAwait(false);
            EnsureLobbyCommandSucceeded(result);
        }

        internal static void EnsureLobbyCommandSucceeded(LcuHttpResult result)
        {
            using Stream? response = result.Content;
            if (result.Failure != LcuHttpFailure.None || result.StatusCode is null ||
                (int)result.StatusCode is < 200 or >= 300)
                throw new HttpRequestException($"LCU 大厅操作失败：{result.Failure}", null, result.StatusCode);
        }

        /// <summary>
        /// 获取大厅实时信息（含双方玩家）。
        /// </summary>
        public static async Task<LobbyGameInfo?> GameNowServer(CancellationToken cancellationToken = default)
        {
            using HttpClientHelper client = new HttpClientHelper();
            using Stream? responseStream = await client.GetAsync("/lol-lobby/v2/lobby", cancellationToken: cancellationToken).ConfigureAwait(false);
            if (responseStream == null) return null;
            return await responseStream.ReadAsJsonAsync<LobbyGameInfo>(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 获取游戏流程阶段（如 Lobby / ChampSelect / InProgress）。
        /// </summary>
        public static async Task<string?> GameFlowPhaseServer(CancellationToken cancellationToken = default)
        {
            using HttpClientHelper client = new HttpClientHelper();
            using Stream? responseStream = await client.GetAsync("/lol-gameflow/v1/gameflow-phase", cancellationToken: cancellationToken).ConfigureAwait(false);
            if (responseStream == null) return null;
            return await responseStream.ReadAsJsonAsync<string>(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 对局进行中获取实时队伍信息。
        /// </summary>
        public static async Task<GameSessionResponse?> GameLineInfoServer(CancellationToken cancellationToken = default)
        {
            HttpClientHelper client = new HttpClientHelper();
            using Stream? responseStream = await client.GetAsync("/lol-gameflow/v1/session", cancellationToken: cancellationToken).ConfigureAwait(false);
            if (responseStream == null) return null;
            return await responseStream.ReadAsJsonAsync<GameSessionResponse>(cancellationToken).ConfigureAwait(false);
        }
    }
}
