using LOL_GameAssistant.LoLApi;
using Newtonsoft.Json;
using System.Linq;
using System.Text.Json.Serialization;
using static LOL_GameAssistant.Model.PlayerModel;

namespace LOL_GameAssistant
{
    public partial class GameMain : AntdUI.Window
    {
        public GameMain()
        {
            InitializeComponent();

        }

        private void GameMain_Load(object sender, EventArgs e)
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
            }
            //��ȡ��ǰ�ٻ�ʦ��Ϣ
            Plyaer? userinfo = JsonConvert.DeserializeObject<Plyaer>(Assets_api.GetUser());
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
                this.play_jd.Value =  (float)userinfo.xpUntilNextLevel   / (float)(userinfo.xpSinceLastLevel + userinfo.xpUntilNextLevel);

            }
        }
    }
}
