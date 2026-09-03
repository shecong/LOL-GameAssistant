using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.LoLApi
{
    /// <summary>
    /// Game_Api 好友部分：好友列表及在线状态。
    /// </summary>
    public static partial class Game_Api
    {
        /// <summary>
        /// 获取当前客户端好友列表。
        /// </summary>
        public static async Task<List<FriendModel>> GetFriendsAsync()
        {
            HttpClentHelper client = new HttpClentHelper();
            Stream? responseStream = await client.GetAsync("/lol-chat/v1/friends");
            if (responseStream == null) return new List<FriendModel>();

            return await responseStream.ReadAsJsonAsync<List<FriendModel>>()
                ?? new List<FriendModel>();
        }
    }
}
