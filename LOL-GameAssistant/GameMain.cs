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
           string userinfo= Assets_api.GetUser();
            AntdUI.Message.info(this, userinfo);
        }
    }
}
