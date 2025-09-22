using LOL_GameAssistant.LoLApi;

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
           string userinfo= Assets_api.GetUser();
            AntdUI.Message.info(this, userinfo);
        }
    }
}
