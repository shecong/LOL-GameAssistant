using LOL_GameAssistant.LoLApi;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace LOL_GameAssistant.BaseViewForm
{
    public partial class LivePlayersForm : UserControl
    {
        public LivePlayersForm(string gametype, string name, string gamedate, string kda, string iswin)
        {
            InitializeComponent();
            Load(name, gametype, gamedate, kda, iswin);
        }

        private async void Load(string gametype, string name, string gamedate, string kda, string iswin)
        {
            this.gametype.Text = "";
            this.name.Text = name;
            this.gamedate.Text = gamedate;
            this.kda.Text = kda;
            this.iswin.Text = iswin;
        }
    }
}