using System.Drawing.Drawing2D;
using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.LoLApi;

namespace LOL_GameAssistant.BaseViewForm
{
    /// <summary>
    /// 单场战绩行：英雄头像 + 英雄名 + 模式 + 日期 + KDA + 胜负，带圆角/悬停动效。
    /// </summary>
    public partial class RecentMatchRow : UserControl
    {
        public const int RowHeight = 40;

        private GameDetailModel.GameInfo? _detail;
        private string? _puuid;
        private Color _baseBack = Color.FromArgb(250, 250, 250);
        private Color _hoverBack = Color.FromArgb(235, 242, 252);
        private Color _accent = Color.FromArgb(150, 150, 150);

        private readonly System.Windows.Forms.Timer _hoverTimer;
        private Color _hoverFrom;
        private Color _hoverTo;
        private bool _hoverActive;
        private double _hoverT;

        public RecentMatchRow()
        {
            InitializeComponent();
            SetStyle(
                ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            _hoverTimer = new System.Windows.Forms.Timer { Interval = 15 };
            _hoverTimer.Tick += (_, _) => HoverTick();
            Disposed += (_, _) => _hoverTimer.Dispose();

            var tip = new ToolTip();
            tip.SetToolTip(picChampion, "双击查看对局详情");

            this.DoubleClick += (_, _) => OpenDetail();
            this.MouseEnter += (_, _) => StartHover(true);
            this.MouseLeave += (_, _) => StartHover(false);
            foreach (Control child in Controls)
            {
                child.DoubleClick += (_, _) => OpenDetail();
                child.MouseEnter += (_, _) => StartHover(true);
                child.MouseLeave += (_, _) => StartHover(false);
            }
        }

        /// <summary>
        /// 根据当前宽度自适应分布各列：左侧固定信息区，时长靠右，
        /// 中间的留白随宽度自然伸展，全屏时也不会显得拥挤。
        /// </summary>
        private void LayoutRow()
        {
            int w = Math.Max(480, Width);
            int x = 10;
            int iconY = Math.Max(3, (Height - 34) / 2);
            int labelY = Math.Max(6, (Height - 20) / 2);

            picChampion.Location = new Point(x, iconY);
            x += 34 + 8;

            lblResult.Location = new Point(x, labelY);
            x += 52;

            lblChampion.Location = new Point(x, labelY);
            x += 106;

            lblMode.Location = new Point(x, labelY);
            lblMode.Width = 160;
            x += 160;

            lblDate.Location = new Point(x, labelY);
            lblDate.Width = 80;
            x += 80;

            lblKda.Location = new Point(x, labelY);
            lblKda.Width = 90;
            x += 90;

            // 时长固定宽度、右对齐，撑开中间留白
            const int durationWidth = 92;
            lblDuration.Location = new Point(Math.Max(x + 8, w - durationWidth - 12), labelY);
            lblDuration.Width = durationWidth;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutRow();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            LayoutRow();
        }

        public async Task SetDataAsync(GameDetailModel.GameInfo detail, GameDetailModel.ParticipantsItem gamer, string? puuid)
        {
            try
            {
                _detail = detail;
                _puuid = puuid;

                bool win = gamer.IsWin();
                _baseBack = win ? Color.FromArgb(236, 247, 238) : Color.FromArgb(253, 238, 238);
                _hoverBack = win ? Color.FromArgb(224, 243, 228) : Color.FromArgb(251, 228, 228);
                _accent = win ? Color.FromArgb(76, 175, 80) : Color.FromArgb(229, 57, 53);
                BackColor = _baseBack;

                lblResult.Text = win ? "胜利" : "失败";
                lblResult.ForeColor = win ? Color.FromArgb(46, 125, 50) : Color.FromArgb(198, 40, 40);
                lblChampion.Text = ChampionMap.GetChampion(gamer.championId)?.RealName ?? $"英雄{gamer.championId}";
                string modeText = detail.GetModeText();
                lblMode.Text = modeText;
                lblDate.Text = detail.gameCreationDate?.Length >= 10 ? detail.gameCreationDate.Substring(0, 10) : "未知";
                lblKda.Text = gamer.GetKdaText();
                lblDuration.Text = detail.GetDurationText();

                string champName = ChampionMap.GetChampion(gamer.championId)?.RealName ?? $"英雄{gamer.championId}";
                var tip = new ToolTip();
                tip.SetToolTip(picChampion, $"{champName} · {modeText} · {detail.GetDurationText()}\n{gamer.GetKdaText()} · {(win ? "胜利" : "失败")}");

                var icon = await Game_Api.GetGameChampionIconAsync(gamer.championId);
                if (icon != null && !IsDisposed)
                {
                    picChampion.Image = icon;
                }
                Invalidate();
            }
            catch
            {
                // 单行加载失败不影响其它行
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(1, 1, Width - 3, Height - 3);
            using var path = GradientPanel.RoundedRect(rect, 8);
            using var fill = new SolidBrush(BackColor);
            g.FillPath(fill, path);

            // 左侧胜负色条
            using var accentBrush = new SolidBrush(_accent);
            g.FillRectangle(accentBrush, 3, 9, 4, Height - 18);

            using var borderPen = new Pen(Color.FromArgb(28, 0, 0, 0), 1);
            g.DrawPath(borderPen, path);

            base.OnPaint(e);
        }

        private void StartHover(bool hovering)
        {
            _hoverFrom = BackColor;
            _hoverTo = hovering ? _hoverBack : _baseBack;
            _hoverT = 0;
            _hoverActive = true;
            _hoverTimer.Start();
        }

        private void HoverTick()
        {
            if (!_hoverActive) return;
            _hoverT = Math.Min(1, _hoverT + 0.14);
            BackColor = UiAnimation.LerpColor(_hoverFrom, _hoverTo, UiAnimation.EaseOutCubic(_hoverT));
            if (_hoverT >= 1)
            {
                _hoverActive = false;
                _hoverTimer.Stop();
            }
        }

        private void OpenDetail()
        {
            if (_detail == null || string.IsNullOrEmpty(_puuid)) return;
            MatchDetailForm.OpenAndHandle(_detail, _puuid, this);
        }
    }
}
