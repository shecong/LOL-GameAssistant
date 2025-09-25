using LOL_GameAssistant.LoLApi;
using LOL_GameAssistant.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LOL_GameAssistant
{
    public partial class recordForm : UserControl
    {
        public recordForm()
        {
            InitializeComponent();
        }

        private void recordForm_Load(object sender, EventArgs e)
        {
        }

        /// <summary>
        /// 加载信息
        /// </summary>
        public void setInfo(GameHeadModel.GameInfo head)
        {
            if (head == null) return;
            //根据表头获取明细信息
            GameDetailModel.GameInfo gameInfo = new GameDetailModel.GameInfo();
            gameInfo = Game_Api.GetGameDetail(Convert.ToString(head.GameId));
            //处理信息

            //加载信息
        }
    }
}