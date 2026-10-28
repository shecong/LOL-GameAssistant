using ToolTip = LOL_GameAssistant.Helper.UiToolTip;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Helper;
using System.Drawing.Drawing2D;

namespace LOL_GameAssistant.BaseViewForm
{
    /// <summary>
    /// 单场战绩摘要行：英雄、模式、KDA、胜负及时长；打开时按需读取完整详情。
    /// </summary>
    public partial class RecentMatchRow : UserControl, IThemeAware
    {
        public const int RowHeight = 60;
        private readonly MatchHonorBadges _honorBadges = new();
        private int ContentHeight => Math.Max(1, Height - (_referenceLayout && !_workspaceLayout ? 0 : _honorBadges.Height));
        internal MatchHonors Honors => _honorBadges.Honors;
        private static readonly SemaphoreSlim HonorDetailGate = new(2);
        private bool _automaticHonors, _needsHonorDetail, _honorLoadStarted;
        private long _honorRevision;

        private MatchDetail? _detail;
        private long _summaryGameId;
        private bool _openingDetail;
        private readonly IMatchHistoryService? _matchHistoryService;
        private readonly CancellationTokenSource _lifetime = new();
        private string? _puuid;
        private readonly IGameAssetService _gameAssetService;

        private readonly ToolTip _championTip = new();
        private Image? _ownedChampionImage;

        private bool _championAndModeOnly;

        private Color _baseBack = Color.FromArgb(250, 250, 250);
        private Color _hoverBack = Color.FromArgb(235, 242, 252);
        private Color _accent = Color.FromArgb(150, 150, 150);

        private readonly System.Windows.Forms.Timer _hoverTimer;
        private Color _hoverFrom;
        private Color _hoverTo;
        private bool _hoverActive;
        private double _hoverT;
        private bool _isHovered;
        private bool _isWin;
        private bool _hasWinResult = true;

        /// <summary>初始化 RecentMatchRow 的实例状态。</summary>
        public RecentMatchRow() : this(AppCompositionRoot.GameAssetService)
        {
        }

        /// <summary>战绩行通过应用端口读取英雄资源，避免直接依赖静态 LCU API。</summary>
        internal RecentMatchRow(
            IGameAssetService gameAssetService,
            IMatchHistoryService? matchHistoryService = null)
        {
            _gameAssetService = gameAssetService;
            _matchHistoryService = matchHistoryService;
            InitializeComponent();
            Height = RowHeight;
            Controls.Add(_honorBadges);

            SetStyle(
                ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            components ??= new System.ComponentModel.Container();
            _hoverTimer = new System.Windows.Forms.Timer(components) { Interval = 15 };
            _hoverTimer.Tick += (_, _) => HoverTick();
            Disposed += (_, _) => _hoverTimer.Dispose();
            HandleDestroyed += (_, _) => StopHover();
            VisibleChanged += (_, _) => { if (!Visible) StopHover(); };

            _championTip.SetToolTip(picChampion, "双击查看对局详情");
            Disposed += (_, _) => { _lifetime.Cancel(); _lifetime.Dispose(); _championTip.Dispose(); _ownedChampionImage?.Dispose(); };

            this.DoubleClick += (_, _) => OpenDetail();
            this.MouseEnter += (_, _) => StartHover(true);
            this.MouseLeave += (_, _) => UpdateHoverState(Cursor.Position);
            foreach (Control child in Controls)
            {
                child.DoubleClick += (_, _) => OpenDetail();
                child.MouseEnter += (_, _) => StartHover(true);
                child.MouseLeave += (_, _) => UpdateHoverState(Cursor.Position);
            }
        }

        /// <summary>
        /// 根据当前宽度自适应分布各列：左侧固定信息区，时长靠右，
        /// 中间的留白随宽度自然伸展，全屏时也不会显得拥挤。
        /// </summary>
        private void LayoutRow()
        {
            LayoutMainRow();
            if (_referenceLayout && !_workspaceLayout)
            {
                // 荣誉与模式并排，无荣誉时不显示占位文字。
                _honorBadges.Visible = Honors.Labels.Count > 0;
                _honorBadges.SetBounds(ClientSize.Width - 132, 26, 112, 20);
            }
            else
            {
                _honorBadges.Visible = true;
                _honorBadges.SetBounds(48, ContentHeight, Math.Max(0, ClientSize.Width - 64), _honorBadges.Height);
            }
        }

        private void LayoutMainRow()
        {
            if (_workspaceLayout) { LayoutWorkspaceRow(); return; }
            if (_referenceLayout) { LayoutReferenceRow(); return; }
            int w = Math.Max(1, ClientSize.Width);
            int x = 10;
            int iconY = Math.Max(3, (ContentHeight - 34) / 2);
            int labelY = Math.Max(6, (ContentHeight - 20) / 2);

            picChampion.Location = new Point(x, iconY);
            x += 34 + (_championAndModeOnly ? 6 : 8);

            lblResult.Location = new Point(x, labelY);
            lblResult.Width = _championAndModeOnly
                ? Math.Max(30, TextRenderer.MeasureText(lblResult.Text, lblResult.Font,
                    Size.Empty, TextFormatFlags.NoPadding).Width + 2) : 44;
            x += lblResult.Width + 4;

            int right = Math.Max(x, w - (_championAndModeOnly ? 8 : 12));
            // 按实际文字保留必要宽度，长 KDA/时长仍能完整显示。
            int durationWidth = _championAndModeOnly
                ? Math.Max(42, TextRenderer.MeasureText(lblDuration.Text, lblDuration.Font,
                    Size.Empty, TextFormatFlags.NoPadding).Width + 4) : w >= 520 ? 82 : 62;
            int kdaWidth = _championAndModeOnly
                ? Math.Max(64, TextRenderer.MeasureText(lblKda.Text, lblKda.Font,
                    Size.Empty, TextFormatFlags.NoPadding).Width + 4) : w >= 520 ? 90 : 82;
            int columnGap = _championAndModeOnly ? 4 : 6;
            int durationLeft = right - durationWidth;
            int kdaLeft = durationLeft - columnGap - kdaWidth;
            bool showDate = !_championAndModeOnly && w >= 440;
            int dateWidth = showDate ? 78 : 0;
            int dateLeft = kdaLeft - 6 - dateWidth;
            int detailsRight = showDate ? dateLeft - 6 : kdaLeft - columnGap;
            int detailsWidth = Math.Max(0, detailsRight - x);

            // 对局玩家卡片在同一列分两行展示英雄、模式，窄卡片也保留模式。
            bool showMode = !_championAndModeOnly && detailsWidth >= 200;
            int championWidth = showMode
                ? Math.Min(150, Math.Max(92, detailsWidth / 2 - 3))
                : detailsWidth;
            lblChampion.Location = new Point(x, _championAndModeOnly ? Math.Max(2, (ContentHeight - 34) / 2) : labelY);
            lblChampion.Width = championWidth;
            lblChampion.Height = _championAndModeOnly ? 34 : 20;

            lblMode.Visible = showMode;
            if (showMode)
            {
                lblMode.Location = new Point(x + championWidth + 6, labelY);
                lblMode.Width = Math.Max(0, detailsRight - lblMode.Left);
            }

            lblDate.Visible = showDate;
            if (showDate)
            {
                lblDate.Location = new Point(dateLeft, labelY);
                lblDate.Width = dateWidth;
            }

            lblKda.Location = new Point(kdaLeft, labelY);
            lblKda.Width = kdaWidth;
            lblDuration.Location = new Point(durationLeft, labelY);
            lblDuration.Width = durationWidth;

        }

        /// <summary>窗口或控件尺寸变化时重新计算内容布局。</summary>
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutRow();
        }

        /// <summary>布局发生变化后更新内部控件尺寸。</summary>
        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            LayoutRow();
        }

        /// <summary>列表只绑定查询者本人的摘要；完整十人详情留到用户打开时获取。</summary>
        public async Task SetSummaryAsync(MatchHistoryGame summary, string puuid)
        {
            long revision = ++_honorRevision;
            _honorLoadStarted = false;
            _detail = null;
            _summaryGameId = summary.GameId;
            _puuid = puuid;
            _championAndModeOnly = true;
            var participant = summary.GetParticipant(puuid);
            _needsHonorDetail = !summary.HasFullParticipants && summary.IsCompletedGame() &&
                participant?.stats is { HasWinResult: true, HasKdaResult: true };
            SetHonors(MatchHonorCalculator.ForPlayer(summary, participant));
            _hasWinResult = participant?.stats?.HasWinResult == true;
            _isWin = _hasWinResult && participant!.IsWin();
            lblResult.Text = !_hasWinResult ? "未知" : _isWin ? "胜利" : "失败";
            int champion = participant?.championId ?? 0;
            string name = champion > 0 ? GetChampionDisplayName(champion) : "英雄未知";
            string mode = summary.GetModeText();
            lblChampion.Text = _referenceLayout ? name : $"{name}\n{mode}";
            lblMode.Text = mode;
            lblKda.Text = participant?.stats?.HasKdaResult == true ? participant.GetKdaText() : "—/—/—";
            lblDuration.Text = summary.GameDuration > 0 ? $"{summary.GameDuration / 60}:{summary.GameDuration % 60:D2}" : "—";
            lblDate.Text = _workspaceLayout && summary.GameCreation > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(summary.GameCreation).ToLocalTime().ToString("MM-dd HH:mm") : "";
            _championTip.SetToolTip(picChampion, $"{name} · {mode}\n{lblKda.Text} · {lblDuration.Text}\n双击获取完整对局详情");
            _championTip.SetToolTip(lblChampion, "双击获取完整对局详情");
            ApplyTheme(UiTheme.Palette);
            LayoutRow();
            QueueAutomaticHonors();
            if (champion <= 0) return;
            try
            {
                var icon = ToImage(await _gameAssetService.GetChampionIconAsync(champion, _lifetime.Token));
                if (IsDisposed || revision != _honorRevision) { icon?.Dispose(); return; }
                picChampion.Image = icon;
                _ownedChampionImage?.Dispose();
                _ownedChampionImage = icon;
            }
            catch (OperationCanceledException) when (IsDisposed) { }
            catch (Exception error) { RuntimeDiagnostics.Report("近期战绩图标", "暂不可用", error.GetType().Name); }
        }

        private void SetHonors(MatchHonors honors)
        {
            _honorBadges.Honors = honors;
            _honorBadges.StatusText = honors.MvpEvaluated ? "本局暂无荣誉" : "MVP 数据不足";
            _honorBadges.AccessibleName = honors.Labels.Count > 0 ? honors.Text : _honorBadges.StatusText;
            LayoutRow();
            _championTip.SetToolTip(_honorBadges, honors.Text + "\n" + MatchHonorCalculator.Explanation +
                (honors.MvpEvaluated ? "" : "\nMVP 暂无法评选，需完整双方数据与明确胜负。"));
            _honorBadges.Invalidate();
        }

        internal void EnableAutomaticHonors()
        {
            _automaticHonors = true;
            QueueAutomaticHonors();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            QueueAutomaticHonors();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            QueueAutomaticHonors();
        }

        private void QueueAutomaticHonors()
        {
            if (!_automaticHonors || !_needsHonorDetail || _honorLoadStarted || !IsHandleCreated ||
                !Visible || IsDisposed || string.IsNullOrEmpty(_puuid) || _summaryGameId <= 0) return;
            _honorLoadStarted = true;
            long revision = _honorRevision;
            _honorBadges.StatusText = "荣誉读取中…";
            if (Honors.Labels.Count == 0) _honorBadges.AccessibleName = _honorBadges.StatusText;
            _honorBadges.Invalidate();
            BeginInvoke(async () => await LoadAutomaticHonorsAsync(revision));
        }

        private async Task LoadAutomaticHonorsAsync(long revision)
        {
            if (IsDisposed || revision != _honorRevision) return;
            bool acquired = false;
            try
            {
                await HonorDetailGate.WaitAsync(_lifetime.Token);
                acquired = true;
                if (IsDisposed || revision != _honorRevision) return;
                var detail = await LoadDetailAsync();
                if (!IsDisposed && revision == _honorRevision && detail == null)
                {
                    _honorBadges.StatusText = "荣誉暂不可用";
                    if (Honors.Labels.Count == 0) _honorBadges.AccessibleName = _honorBadges.StatusText;
                    _honorBadges.Invalidate();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!IsDisposed && revision == _honorRevision)
                {
                    _honorBadges.StatusText = "荣誉暂不可用";
                    if (Honors.Labels.Count == 0) _honorBadges.AccessibleName = _honorBadges.StatusText;
                    _honorBadges.Invalidate();
                    RuntimeDiagnostics.Report("近期战绩荣誉", "暂不可用", error.Message);
                }
            }
            finally { if (acquired) HonorDetailGate.Release(); }
        }

        /// <summary>根据英雄标识取得展示名称。</summary>
        private static string GetChampionDisplayName(int championId)
        {
            string name = AppCompositionRoot.ChampionCatalog.GetDisplayName(championId);
            return string.IsNullOrWhiteSpace(name) ? $"英雄{championId}" : name;
        }

        /// <summary>资源服务返回原始内容，由表现层解码为控件所需的位图。</summary>
        private static Image? ToImage(GameAsset? asset)
        {
            try
            {
                if (asset == null || asset.IsEmpty) return null;
                using var stream = new MemoryStream(asset.Content);
                using var source = Image.FromStream(stream);
                return new Bitmap(source);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>绘制当前控件的自定义外观。</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            // 滑入/布局过程中行高可能被压到极小，Width-3 / Height-3 会变成 0 或负数，
            // GDI+ 不接受空矩形，会抛 ArgumentException。
            if (Width <= 3 || Height <= 3) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(1, 1, Width - 3, Height - 3);
            using var path = GradientPanel.RoundedRect(rect, 8);
            using var fill = new SolidBrush(BackColor);
            g.FillPath(fill, path);

            // 左侧胜负色条
            using var accentBrush = new SolidBrush(_accent);
            g.FillRectangle(accentBrush, 3, 9, 4, Height - 18);

            using var borderPen = new Pen(_referenceLayout ? UiTheme.Palette.Border : Color.FromArgb(28, 0, 0, 0), 1);
            g.DrawPath(borderPen, path);

            base.OnPaint(e);
        }

        /// <summary>将语义主题颜色应用到当前控件或窗口。</summary>
        public void ApplyTheme(ThemePalette palette)
        {
            _hoverTimer.Stop();
            _hoverActive = false;
            _isHovered = false;
            _baseBack = palette.IsDark ? (_referenceLayout ? Color.FromArgb(25, 38, 47) : palette.SurfaceRaised) : _isWin
                ? Color.FromArgb(236, 247, 238) : Color.FromArgb(253, 238, 238);
            _accent = !_hasWinResult ? palette.TextSecondary : _isWin ? Color.FromArgb(76, 175, 80) : Color.FromArgb(229, 57, 53);
            _hoverBack = UiAnimation.LerpColor(_baseBack, _accent, palette.IsDark ? .14 : .08);
            BackColor = _baseBack;
            _honorBadges.BackColor = _baseBack;
            _honorBadges.Invalidate();
            if (_referenceLayout)
            {
                foreach (AntdUI.Label label in Controls.OfType<AntdUI.Label>()) label.BackColor = Color.Transparent;
                picChampion.BackColor = _baseBack;
                lblChampion.ForeColor = lblKda.ForeColor = palette.TextPrimary;
                lblDuration.ForeColor = lblMode.ForeColor = palette.TextSecondary;
                lblResult.BackColor = UiAnimation.LerpColor(_baseBack, _accent, .22);
                lblResult.ForeColor = _accent;
                if (_detailArrow != null) _detailArrow.ForeColor = palette.TextSecondary;
            }
        }

        /// <summary>根据鼠标位置更新战绩行的悬停状态。</summary>
        internal void UpdateHoverState(Point screenPosition)
        {
            if (IsDisposed || Disposing) return;
            bool inside = IsHandleCreated && Visible && RectangleToScreen(ClientRectangle).Contains(screenPosition);
            if (inside)
            {
                StartHover(true);
                return;
            }
            _isHovered = false;
            _hoverActive = false;
            _hoverTimer.Stop();
            BackColor = _baseBack;
        }

        /// <summary>启动战绩行悬停动画。</summary>
        private void StartHover(bool hovering)
        {
            if (IsDisposed || Disposing || !IsHandleCreated || !Visible) return;
            if (_isHovered == hovering) return;
            _isHovered = hovering;
            _hoverFrom = BackColor;
            _hoverTo = hovering ? _hoverBack : _baseBack;
            _hoverT = 0;
            _hoverActive = true;
            _hoverTimer.Start();
        }

        private void StopHover()
        {
            _hoverTimer.Stop();
            _isHovered = false;
            _hoverActive = false;
            if (!IsDisposed && !Disposing) BackColor = _baseBack;
        }

        /// <summary>推进悬停动画并重绘战绩行。</summary>
        private void HoverTick()
        {
            if (IsDisposed || Disposing || !IsHandleCreated || !Visible)
            {
                _hoverTimer.Stop();
                return;
            }
            // MouseLeave can be missed when scrolling or moving across child windows.
            UpdateHoverState(Cursor.Position);
            if (!_hoverActive) return;
            _hoverT = Math.Min(1, _hoverT + 0.14);
            BackColor = UiAnimation.LerpColor(_hoverFrom, _hoverTo, UiAnimation.EaseOutCubic(_hoverT));
            if (_hoverT >= 1)
            {
                _hoverActive = false;
                if (!_isHovered) _hoverTimer.Stop();
            }
        }

        /// <summary>打开当前对局的完整详情。</summary>
        internal async Task<MatchDetail?> LoadDetailAsync()
        {
            if (_detail != null) return _detail;
            if (_summaryGameId <= 0 || IsDisposed || string.IsNullOrEmpty(_puuid)) return null;
            long revision = _honorRevision;
            string session = PlayerHistorySession.Key;
            var detail = await PlayerHistoryRepository.For(_matchHistoryService ?? AppCompositionRoot.MatchHistoryService)
                .GetDetailAsync(_summaryGameId, _lifetime.Token);
            if (IsDisposed || revision != _honorRevision || session != PlayerHistorySession.Key) return null;
            if (detail?.GameId == _summaryGameId && detail.GetParticipant(_puuid) is { } player)
            {
                _detail = detail;
                if (!IsDisposed) SetHonors(MatchHonorCalculator.ForPlayer(detail, player));
            }
            return _detail;
        }

        private async void OpenDetail()
        {
            if (_openingDetail || IsDisposed || string.IsNullOrEmpty(_puuid)) return;
            _openingDetail = true;
            if (_detailArrow != null) _detailArrow.Text = "…";
            try
            {
                var detail = await LoadDetailAsync();
                if (IsDisposed) return;
                if (detail != null) MatchDetailForm.OpenAndHandle(detail, _puuid, this);
                else _championTip.SetToolTip(lblChampion, "详情暂不可用，双击重试");
            }
            catch (OperationCanceledException) when (IsDisposed) { }
            catch (Exception error)
            {
                RuntimeDiagnostics.Report("打开对局详情", "暂不可用", error.Message);
                if (!IsDisposed) _championTip.SetToolTip(lblChampion, "详情获取失败，双击重试");
            }
            finally
            {
                _openingDetail = false;
                if (!IsDisposed && _detailArrow != null) _detailArrow.Text = "›";
            }
        }
    }
}
