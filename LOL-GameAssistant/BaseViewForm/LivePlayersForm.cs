using LOL_GameAssistant.LoLApi;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using static System.Net.Mime.MediaTypeNames;

namespace LOL_GameAssistant.BaseViewForm
{
    public partial class LivePlayersForm : UserControl
    {
        public LivePlayersForm(String Type, string gametype, string name, string gamedate, string kda, string iswin)
        {
            InitializeComponent();
            Load(Type, gametype, name, gamedate, kda, iswin);
        }

        private async void Load(string Type, string gametype, string name, string gamedate, string kda, string iswin)
        {
            if (Type == "头部")
            {
                this.gridPanel1.Controls.Clear();
                this.gridPanel1.Span = "80%";
                var labe = new AntdUI.Label() { Text = gametype, SuffixSvg = "CopyOutlined" };
                labe.Click += async (s, e) =>
                {
                    Clipboard.SetText(name);
                    AntdUI.Message.success(ParentForm!, "召唤师id已复制到剪贴板！");
                };
                this.gridPanel1.Controls.Add(labe);
            }
            else
            {
                this.BackColor = iswin == "win" ? System.Drawing.Color.FromArgb(225, 245, 233) : System.Drawing.Color.FromArgb(254, 235, 234);
                this.gametype.Text = gametype;

                this.name.Text = name;
                this.gamedate.Text = gamedate;
                this.kda.Text = kda;
                this.iswin.Text = iswin;
            }
        }
    }
}