using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.LoLApi;
using System.Data;

namespace LOL_GameAssistant.BaseViewForm
{
    public partial class LivePlayerForm : UserControl
    {
        private string? Playerpuuid;

        public LivePlayerForm(string _Playerpuuid)
        {
            InitializeComponent();
            Playerpuuid = _Playerpuuid;
            this.Load += LivePlayerForm_Load;
        }

        /// <summary>
        /// 安全的 Image.FromStream 替代
        /// </summary>
        private static Image LoadImageSafe(Stream stream)
        {
            var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;
            return Image.FromStream(ms);
        }

        private async void LivePlayerForm_Load(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(Playerpuuid))
                {
                    return;
                }

                // 获取玩家近十场的游戏记录
                String begIndex = "0";
                String endIndex = "10";
                GameHeadModel.MatchHistoryResponse? matchlists = await Game_Api.GetUserGame(Playerpuuid, begIndex, endIndex);

                if (matchlists?.Games?.Games == null) return;

                var sortedList = matchlists.Games.Games
                   .OrderByDescending(p => p.GameCreation).Take(9)
                   .OrderBy(p => p.GameCreation)
                   .ToList();

                // 逐场加载游戏数据
                for (int i = 0; i < sortedList.Count; i++)
                {
                    GameDetailModel.GameInfo? gameInfo = await Game_Api.GetGameDetail(Convert.ToString(sortedList[i].GameId));
                    if (gameInfo == null) continue;

                    // 找到当前玩家的参与者数据
                    var identity = gameInfo.participantIdentities
                        ?.FirstOrDefault(p => p?.player?.puuid == Playerpuuid);
                    if (identity == null) continue;

                    GameDetailModel.ParticipantsItem? gamer = gameInfo.participants
                        ?.FirstOrDefault(p => p?.participantId == identity.participantId);
                    if (gamer?.stats == null) continue;

                    // 日期安全检查
                    string gameDate = (gameInfo.gameCreationDate?.Length >= 10)
                        ? gameInfo.gameCreationDate.Substring(0, 10)
                        : "";

                    // 胜负判断（大小写不敏感）
                    bool isWin = string.Equals(gamer.stats.win, "true", StringComparison.OrdinalIgnoreCase);

                    LivePlayersForm livePlayersForm = new LivePlayersForm(
                        "明细",
                        gameInfo.queueId,
                        ChampionMap.GetChampion(gamer.championId)?.RealName ?? "未知",
                        gameDate,
                        $"{gamer.stats.kills}/{gamer.stats.deaths}/{gamer.stats.assists}",
                        isWin ? "win" : "loss");
                    this.gridPanel1.Controls.Add(livePlayersForm);

                    // 最后一个加载头部
                    if (i == sortedList.Count - 1)
                    {
                        var playername = gameInfo.participantIdentities
                            ?.FirstOrDefault(p => p?.player?.puuid == Playerpuuid)
                            ?.player?.gameName;
                        var puuid = gameInfo.participantIdentities
                            ?.FirstOrDefault(p => p?.player?.puuid == Playerpuuid)
                            ?.player?.puuid;

                        LivePlayersForm palyerLive = new LivePlayersForm("头部", playername, puuid, "", "", "");
                        this.gridPanel1.Controls.Add(palyerLive);
                    }
                }
            }
            catch (Exception ex)
            {
                GameMain.infoMsg.AddMsg($"加载玩家近场记录失败: {ex.Message}");
            }
        }
    }
}
