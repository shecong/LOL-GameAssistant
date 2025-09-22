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
            //获取客户端登陆
            (string? port, string? token) = GetlolLcu.GetlolLcuCmd();
            if (string.IsNullOrEmpty(port) || string.IsNullOrEmpty(token))
            {
                AntdUI.Message.error(this, "未找到正在运行的LOL客户端，请确保客户端已启动并登录。");
            }
            else
            {
                HttpClentHelper.Port = port;
                HttpClentHelper.Token = token;
            }
            //获取当前召唤师信息
            Plyaer? userinfo = JsonConvert.DeserializeObject<Plyaer>(Assets_api.GetUser());
            if (userinfo != null)
            {
                //获取头像
                Stream headicon = Assets_api.GetImg(userinfo.profileIconId);
                if (headicon != null)
                {
                    // 使用 Image.FromStream() 方法将 Stream 转换为 Image
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
