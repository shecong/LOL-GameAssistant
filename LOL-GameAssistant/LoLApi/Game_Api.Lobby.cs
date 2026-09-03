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
        public static async Task OpenGameServer()
        {
            HttpClentHelper client = new HttpClentHelper();
            _ = await client.PostAsync("/lol-lobby/v2/lobby/matchmaking/search");
        }

        /// <summary>
        /// 接受当前匹配确认。
        /// </summary>
        public static async Task GameTrueServer()
        {
            HttpClentHelper client = new HttpClentHelper();
            _ = await client.PostAsync("/lol-matchmaking/v1/ready-check/accept");
        }

        /// <summary>
        /// 获取大厅实时信息（含双方玩家）。
        /// </summary>
        public static async Task<LobbyGameInfo?> GameNowServer()
        {
            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync("/lol-lobby/v2/lobby");
            if (responseStream == null) return null;
            return await responseStream.ReadAsJsonAsync<LobbyGameInfo>();
        }

        /// <summary>
        /// 获取游戏流程阶段（如 Lobby / ChampSelect / InProgress）。
        /// </summary>
        public static async Task<string?> GameFlowPhaseServer()
        {
            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync("/lol-gameflow/v1/gameflow-phase");
            if (responseStream == null) return null;
            return await responseStream.ReadAsJsonAsync<string>();
        }

        /// <summary>
        /// 对局进行中获取实时队伍信息。
        /// </summary>
        public static async Task<GameSessionResponse?> GameLineInfoServer()
        {
            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync("/lol-gameflow/v1/session");
            if (responseStream == null) return null;
            return await responseStream.ReadAsJsonAsync<GameSessionResponse>();
        }
    }
}
