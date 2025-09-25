using LOL_GameAssistant.LoLApi;
using LOL_GameAssistant.Model;
using LOL_GameAssistant.Models;
using Newtonsoft.Json;
using static LOL_GameAssistant.Model.LolRankedDataParser;
using static LOL_GameAssistant.Model.PlayerModel;

namespace LOL_GameAssistant
{
    public partial class GameMain : AntdUI.Window
    {
        public Plyaer? userinfo = new Plyaer();

        public GameMain()
        {
            InitializeComponent();
        }

        public void GameMain_Load(object sender, EventArgs e)
        {
            //��ȡ�ͻ��˵�½
            (string? port, string? token) = GetlolLcu.GetlolLcuCmd();
            if (string.IsNullOrEmpty(port) || string.IsNullOrEmpty(token))
            {
                AntdUI.Message.error(this, "δ�ҵ��������е�LOL�ͻ��ˣ���ȷ���ͻ�������������¼��");
            }
            else
            {
                HttpClentHelper.Port = port;
                HttpClentHelper.Token = token;
                Console.WriteLine($"{port}:{token}");
            }
            //��ȡ��ǰ�ٻ�ʦ��Ϣ
            userinfo = JsonConvert.DeserializeObject<Plyaer>(Assets_api.GetUser());
            if (userinfo != null)
            {
                //��ȡͷ��
                Stream headicon = Assets_api.GetImg(userinfo.profileIconId);
                if (headicon != null)
                {
                    // ʹ�� Image.FromStream() ������ Stream ת��Ϊ Image
                    Image profileImage = Image.FromStream(headicon);
                    this.play_HeadIcon.Image = profileImage;
                }
                this.play_name.Text = userinfo.gameName;
                this.play_number.Text = $"#{userinfo.tagLine}";
                this.play_QF.Text = "";
                this.play_dj.Text = userinfo.summonerLevel;
                this.play_next.Text = Convert.ToString(userinfo.xpSinceLastLevel);
                this.play_jd.Value = (float)userinfo.xpUntilNextLevel / (float)(userinfo.xpSinceLastLevel + userinfo.xpUntilNextLevel);

                //��ȡ��ǰ�ٻ�ʦ��Ϸ������Ϣ
                GetGameSJ(userinfo);
                //��ȡ��ұ�����¼
                GetGameInfo(userinfo);
            }
        }

        /// <summary>
        /// ��ȡ��ҵı�����¼
        /// </summary>
        /// <param name="userinfo"></param>
        /// <exception cref="NotImplementedException"></exception>
        private void GetGameInfo(Plyaer userinfo)
        {
            if (userinfo == null) return;
            String begIndex = "1";
            String endIndex = "3";

            GameHeadModel.MatchHistoryResponse? matchlists = Game_Api.GetUserGame(userinfo.puuid, begIndex, endIndex);
            //�������ݵ�����
            for (int i = 0; i < matchlists?.Games?.Games?.Count; i++)
            {
                recordForm record = new recordForm();
                record.setInfo(matchlists.Games.Games[i]);
                this.stackPanel1.Controls.Add(record);
            }
        }

        /// <summary>
        /// ��ȡ���������Ϣ
        /// </summary>
        /// <param name="plyaer"></param>
        /// <exception cref="NotImplementedException"></exception>
        private void GetGameSJ(Plyaer? userinfo = null)
        {
            if (userinfo == null) return;
            LolRankedDataParser lolparser = new LolRankedDataParser();
            LolRankedDataParser.RankedData gameinfo = Game_Api.GetUserGame(userinfo.puuid);
            if (gameinfo == null) return;
            //��ȡ��˫����Ϣ
            LolRankedDataParser.RankedEntry solo = lolparser.GetQueueData(gameinfo, QueueTypes.RANKED_SOLO_5x5);
            //��ȡ���5v5��Ϣ
            LolRankedDataParser.RankedEntry flex = lolparser.GetQueueData(gameinfo, QueueTypes.RANKED_FLEX_SR);
            //������Ϣ
            if (solo != null)
            {
                this.pic_dsp.Image = CheckTierImg(solo.Tier);
                this.game_dspT.Text = CheckTierName(solo.Tier) + solo.Division;
                this.game_dsp_sl.Text = Convert.ToString(solo.WinRate);
                this.game_dsp_win.Text = Convert.ToString(solo.CurrentSeasonWinsForRewards);
                this.game_dsp_loss.Text = Convert.ToString(solo.Losses);
            }
            if (flex != null)
            {
                this.pic_lhp.Image = CheckTierImg(flex.Tier);
                this.game_lhpT.Text = CheckTierName(flex.Tier) + flex.Division;
                this.game_lhp_sl.Text = Convert.ToString(flex.WinRate);
                this.game_lhp_win.Text = Convert.ToString(flex.CurrentSeasonWinsForRewards);
                this.game_lhp_loss.Text = Convert.ToString(flex.Losses);
            }
            //��ȡ������Ϣ
            this.game_dws.Text = solo?.ProvisionalGamesRemaining >= 10 ? "��" : "��";
            this.game_jjs.Text = string.IsNullOrEmpty(solo?.MiniSeriesProgress) ? "�Ƕ�����" : "solo.MiniSeriesProgress";
            this.game_jjscount.Text = "δ֪";
            this.game_dqsd.Text = Convert.ToString(solo?.LeaguePoints);
            if (gameinfo.Seasons.TryGetValue("RANKED_SOLO_5x5", out SeasonInfo? value))
            {
                this.game_sjend.Text = Convert.ToString(value.SeasonEndDateTime);
            }

            this.game_ycf.Text = Convert.ToString(solo?.RatedRating);
        }

        /// <summary>
        /// ���ݶ�λ���ض�ӦͼƬ
        /// </summary>
        /// <param name="tier"></param>
        /// <returns></returns>
        private Image CheckTierImg(string tier)
        {
            switch (tier)
            {
                case "IRON":
                    return Properties.Resources._01;

                case "BRONZE":
                    return Properties.Resources._02;

                case "SILVER":
                    return Properties.Resources._03;

                case "GOLD":
                    return Properties.Resources._04;

                case "PLATINUM":
                    return Properties.Resources._05;

                case "DIAMOND":
                    return Properties.Resources._06;

                case "MASTER":
                    return Properties.Resources._07;

                case "GRANDMASTER":
                    return Properties.Resources._08;

                case "CHALLENGER":
                    return Properties.Resources._09;

                default:
                    return Properties.Resources.����;
            }
        }

        /// <summary>
        /// ���ݶ�λ���ض�Ӧ����
        /// </summary>
        /// <param name="tier"></param>
        /// <returns></returns>
        private String CheckTierName(string tier)
        {
            switch (tier)
            {
                case "IRON":
                    return "��ͭ";

                case "BRONZE":
                    return "����";

                case "SILVER":
                    return "����";

                case "GOLD":
                    return "�ƽ�";

                case "PLATINUM":
                    return "����";

                case "DIAMOND":
                    return "שʯ";

                case "MASTER":
                    return "������ʦ";

                case "GRANDMASTER":
                    return "��ʦ";

                case "CHALLENGER":
                    return "��ǿ����";

                default:
                    return "�޶�λ";
            }
        }

        /// <summary>
        /// ˢ������
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void refeash_Click(object sender, EventArgs e)
        {
            //��ȡ��ǰ�ٻ�ʦ��Ϣ
            userinfo = JsonConvert.DeserializeObject<Plyaer>(Assets_api.GetUser(userinfo?.puuid));
            if (userinfo != null)
            {
                //��ȡͷ��
                Stream headicon = Assets_api.GetImg(userinfo.profileIconId);
                if (headicon != null)
                {
                    // ʹ�� Image.FromStream() ������ Stream ת��Ϊ Image
                    Image profileImage = Image.FromStream(headicon);
                    this.play_HeadIcon.Image = profileImage;
                }
                this.play_name.Text = userinfo.gameName;
                this.play_number.Text = $"#{userinfo.tagLine}";
                this.play_QF.Text = "";
                this.play_dj.Text = userinfo.summonerLevel;
                this.play_next.Text = Convert.ToString(userinfo.xpSinceLastLevel);
                this.play_jd.Value = (float)userinfo.xpUntilNextLevel / (float)(userinfo.xpSinceLastLevel + userinfo.xpUntilNextLevel);

                //��ȡ��ǰ�ٻ�ʦ��Ϸ������Ϣ
                GetGameSJ(userinfo);
            }
        }
    }
}