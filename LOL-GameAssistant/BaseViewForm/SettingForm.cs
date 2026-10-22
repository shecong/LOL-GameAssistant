using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.Settings;
using LOL_GameAssistant.Helper;
using Microsoft.Win32;
using System.Diagnostics;

namespace LOL_GameAssistant.BaseViewForm
{
    /// <summary>设置页：读取、编辑并保存助手各功能的配置。</summary>
    public partial class SettingForm : UserControl, IThemeAware
    {

        private AssistantSettings _config;
        private bool _isLoading = true;
        private bool _autoSaveEventsBound;
        private readonly UiRequestScope _lifetime = new();
        private ToolTip toolTip1 = new ToolTip();

        private readonly ToolTip _featureTip = new()
        {
            // 说明文本较长，默认 5 秒往往读不完。
            AutoPopDelay = 20000,
            InitialDelay = 350,
            ReshowDelay = 100,
            ShowAlways = false
        };

        public void ApplyTheme(ThemePalette palette)
        {
            BackColor = palette.Surface;
            _settingsSegmented.BackColor = palette.Surface;
            _settingsSegmented.ForeColor = palette.TextPrimary;
            _settingsSegmentHost.BackColor = palette.SurfaceRaised;
            foreach (Control? segment in _settingsSegments)
            {
                if (segment == null) continue;
                segment.BackColor = palette.SurfaceRaised;
                foreach (TableLayoutPanel layout in segment.Controls.OfType<TableLayoutPanel>())
                    layout.BackColor = palette.SurfaceRaised;
            }
        }
        private readonly Dictionary<(ToolTip Tip, Control Control), string> _originalTips = new();
        private readonly HashSet<Control> _featureTipTargets = [];
        private Control? _activeFeatureTipControl;
        private Control? _activeDesignerTipControl;

        /// <summary>按当前语言刷新界面文本。</summary>
        public void ApplyLanguage()
        {
            HideSettingTips();
            string[] sections = ["对局自动化", "客户端与数据", "外观与窗口", "喊话", "AI 与推荐", "客户端工具"];
            for (int index = 0; index < sections.Length && index < _settingsSegmented.Items.Count; index++)
                _settingsSegmented.Items[index].Text = UiLanguage.T(sections[index]);
            foreach (Control segment in _settingsSegments)
                if (segment != null) ResizeSettingColumns(segment);
            ApplyTips(this, _featureTip);
            ApplyTips(this, toolTip1);
            if (_settingsSegments[5] is ClientToolsForm tools) tools.ApplyLanguage();
        }

        /// <summary>根据设置页可用宽度调整各列大小。</summary>
        private static void ResizeSettingColumns(Control root)
        {
            if (root is TableLayoutPanel layout && layout.ColumnStyles.Count >= 2 &&
                layout.ColumnStyles[0].SizeType == SizeType.Absolute &&
                layout.ColumnStyles[0].Width is >= 110 and <= 250)
                layout.ColumnStyles[0].Width = UiLanguage.IsEnglish ? 245 : 118;
            foreach (Control child in root.Controls) ResizeSettingColumns(child);
        }

        /// <summary>为设置控件绑定对应的功能说明。</summary>
        private void ApplyTips(Control root, ToolTip tip)
        {
            string current = tip.GetToolTip(root) ?? "";
            if (!string.IsNullOrWhiteSpace(current))
            {
                var key = (tip, root);
                if (!_originalTips.TryGetValue(key, out string? original))
                    _originalTips[key] = original = current;
                tip.SetToolTip(root, UiLanguage.T(original));
            }
            foreach (Control child in root.Controls) ApplyTips(child, tip);
        }

        /// <summary>保存所选语言并触发界面文本刷新。</summary>
        private void SwitchLanguage()
        {
            if (_isLoading) return;
            string mode = _languageMode.SelectedIndex switch { 1 => "en-US", 2 => "zh-TW", _ => "zh-CN" };
            if (_config.LanguageMode == mode) return;
            _config.LanguageMode = mode;
            AssistantSettings saved = _settingsStore.Load();
            saved.LanguageMode = mode;
            _settingsStore.Save(saved);
            UiLanguage.SetMode(mode);
            ApplyLanguage();
        }

        private readonly IGameClientLauncher _gameClientLauncher;
        private readonly IApplicationSettingsStore _settingsStore;
        private readonly ISettingsSecretProtector _settingsSecretProtector;
        private readonly AntdUI.Segmented _settingsSegmented = new();
        private readonly AntdUI.Panel _settingsSegmentHost = new();
        private readonly Control[] _settingsSegments = new Control[6];
        private readonly AntdUI.Input _clientPath = new() { Dock = DockStyle.Fill };
        private readonly AntdUI.Checkbox _autoLaunchClient = new() { Text = "启动助手时直接启动 LOL 客户端", AutoSize = true };
        private readonly AntdUI.Label _clientStatus = new() { AutoSize = true, ForeColor = Color.DimGray };
        private readonly AntdUI.Button _launchClientButton = new() { Text = "立即启动 LOL", AutoSize = true };
        private readonly AntdUI.InputNumber _opacity = new()
        {
            Minimum = 40, Maximum = 100, Width = 130,
            Height = UiMetrics.ControlHeight, AlwaysShowControl = true
        };
        // Select.ReadOnly also blocks opening the menu; List prevents typing while allowing selection.
        private readonly AntdUI.Select _themeMode = new() { List = true, DropDownArrow = true, Width = 160, Height = UiMetrics.ControlHeight };
        private readonly AntdUI.Select _languageMode = new() { List = true, DropDownArrow = true, Width = 160, Height = UiMetrics.ControlHeight };
        private readonly AntdUI.Input _hotkey = new() { ReadOnly = true, Width = 160, TabStop = true };
        private readonly AntdUI.Checkbox _battleOverlayEnabled = new() { Text = "启用局内双方对局信息面板", AutoSize = true };
        private readonly AntdUI.Input _battleOverlayHotkey = new() { ReadOnly = true, Width = 200, TabStop = true };
        private readonly AntdUI.Checkbox _onlyLeagueFocused = new() { Text = "仅在 LOL 位于前台时响应", AutoSize = true };
        private QuickShoutForm? _quickShoutForm;
        private readonly AntdUI.Checkbox _recommendationEnabled = new() { Text = "启用 AI 时间线建议", AutoSize = true };
        private readonly AntdUI.Checkbox _opggBuildAssistantEnabled = new() { Text = "启用 OP.GG 选人出装与符文推荐", AutoSize = true };
        private readonly AntdUI.Checkbox _autoApplyRuneBuild = new() { Text = "选人自动应用个人符文；无匹配时使用 OP.GG", AutoSize = true };
        private readonly AntdUI.Checkbox _mayhemOverlayEnabled = new() { Text = "海克斯大乱斗显示局内增幅侧边栏", AutoSize = true };
        private readonly AntdUI.Checkbox _champSelectCompanionEnabled = new() { Text = "选人时在客户端侧边显示队友伴随窗", AutoSize = true };
        private readonly AntdUI.Checkbox _autoSwapAramBench = new() { Text = "按优先级自动交换备战席英雄", AutoSize = true };
        private readonly AntdUI.SelectMultiple _aramBenchPriority = new() { Dock = DockStyle.Fill };
        private readonly AntdUI.Checkbox _champSelectKdaAnnouncementEnabled = new() { Text = "选人加载完成后发送 KDA 评估到聊天", AutoSize = true };
        private readonly AntdUI.Checkbox _gameKdaAnnouncementEnabled = new() { Text = "对局中发送双方玩家近期 KDA 评估", AutoSize = true };
        private readonly AntdUI.Checkbox _gameKdaOnePlayerPerLine = new() { Text = "每名玩家单独发送一条（单人一行）", AutoSize = true };
        private readonly AntdUI.InputNumber _ratingMinimumGames = new() { Name = "ratingMinimumGames", Minimum = 1, Maximum = 50, Value = 10, Width = 100 };
        private readonly AntdUI.Input _champSelectKdaAnnouncementTemplate = new() { Dock = DockStyle.Fill, Multiline = true, Height = 86 };
        private readonly AntdUI.Select _provider = new() { List = true, DropDownArrow = true, Width = 210 };

        // 可下拉可手填：模型名写错时服务端只回一个 400，所以按服务商给出可选值。
        private readonly ComboBox _model = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 290 };

        private readonly AntdUI.Button _testAi = new() { Text = "测试连接", AutoSize = true };
        private readonly AntdUI.Button _fetchModels = new() { Text = "获取可用模型", AutoSize = true };
        private readonly AntdUI.Label _aiTestStatus = new() { AutoSize = true, ForeColor = Color.DimGray };
        private readonly AntdUI.Input _baseUrl = new() { Dock = DockStyle.Fill };
        private readonly AntdUI.Input _apiKey = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true, PlaceholderText = "留空则保留已保存的密钥" };
        private readonly AntdUI.Label _apiKeyStatus = new() { AutoSize = true, ForeColor = Color.DimGray };
        private readonly AntdUI.Checkbox _dynamicRefresh = new() { Text = "游戏中自动刷新建议", AutoSize = true };
        private readonly AntdUI.InputNumber _dynamicSeconds = new() { Minimum = 15, Maximum = 600, Width = 100 };
        private readonly AntdUI.InputNumber _aiMinimumSeconds = new() { Minimum = 8, Maximum = 300, Width = 80 };
        private readonly AntdUI.InputNumber _aiBudget = new() { Minimum = 1, Maximum = 200, Width = 80 };
        private readonly AntdUI.Checkbox _showPopup = new() { Text = "建议刷新后弹出提醒", AutoSize = true };
        private readonly AntdUI.Checkbox _overlayEnabled = new() { Text = "游戏内显示建议浮窗", AutoSize = true };
        private readonly AntdUI.Select _overlayPosition = new() { List = true, DropDownArrow = true, Width = 150 };
        private readonly AntdUI.InputNumber _overlayOffsetX = new() { Minimum = -600, Maximum = 600, Width = 80 };
        private readonly AntdUI.InputNumber _overlayOffsetY = new() { Minimum = -600, Maximum = 600, Width = 80 };
        private readonly AntdUI.InputNumber _overlayDuration = new() { Minimum = 3, Maximum = 30, Width = 80 };
        private bool _clearAiKey;
        private static readonly string[] ResolutionPresets = { "1280x720", "1366x768", "1600x900", "1920x1080", "2560x1440", "3840x2160" };

        /// <summary>初始化 SettingForm 的实例状态。</summary>
        public SettingForm() : this(
            AppCompositionRoot.GameClientLauncher,
            AppCompositionRoot.ApplicationSettingsStore,
            AppCompositionRoot.SettingsSecretProtector)
        {
        }

        /// <summary>设置页通过应用端口读写本地配置与保护敏感字段。</summary>
        internal SettingForm(
            IGameClientLauncher gameClientLauncher,
            IApplicationSettingsStore settingsStore,
            ISettingsSecretProtector settingsSecretProtector,
            LOL_GameAssistant.Application.ClientFeatures.IClientFeatureService? clientFeatures = null)
        {
            _gameClientLauncher = gameClientLauncher;
            _settingsStore = settingsStore;
            _settingsSecretProtector = settingsSecretProtector;
            _matchFeatures = clientFeatures ?? AppCompositionRoot.ClientFeatureService;
            InitializeComponent();
            WindowLayout.ConfigureDpi(this);
            _config = new AssistantSettings();
            // Populate choices before settings hydration, even when their page is not built yet.
            _themeMode.Items.AddRange(new object[] { "跟随系统", "浅色", "深色" });
            _languageMode.Items.AddRange(new object[] { "简体中文", "English", "繁體中文" });
            _provider.Items.AddRange(Enum.GetValues<LOL_GameAssistant.Domain.Settings.AiProvider>().Cast<object>().ToArray());
            _overlayPosition.Items.AddRange(new object[] { "左下", "左上", "右下", "右上", "屏幕中央" });
            _featureTip.Popup += (_, e) => _activeFeatureTipControl = e.AssociatedControl;
            toolTip1.Popup += (_, e) => _activeDesignerTipControl = e.AssociatedControl;
            VisibleChanged += (_, _) => { if (!Visible) HideSettingTips(); };
            MouseLeave += (_, _) => HideSettingTips();
            AttachCommonSegmentTips();
            InitializeSegmentedSettings();
            BindBooleanAutoSave();
            SizeInteractiveControls(this);
            DpiChangedAfterParent += (_, _) => SizeInteractiveControls(this);
            Disposed += (_, _) => { _lifetime.Dispose(); toolTip1.Dispose(); _featureTip.Dispose(); };
        }

        private void SizeInteractiveControls(Control root)
        {
            foreach (Control child in root.Controls)
            {
                if (child is AntdUI.Input or AntdUI.InputNumber or AntdUI.Select or AntdUI.SelectMultiple or AntdUI.Button)
                    child.Height = Math.Max(child.Height, (child is AntdUI.SelectMultiple ? 48 : UiMetrics.ControlHeight) * DeviceDpi / 96);
                SizeInteractiveControls(child);
                if (child is FlowLayoutPanel flow && flow.Controls.Cast<Control>().Any(c => c is AntdUI.Input or AntdUI.InputNumber or AntdUI.Button))
                    flow.Height = Math.Max(flow.Height, flow.Controls.Cast<Control>().Max(c => c.Bottom + c.Margin.Bottom) + flow.Padding.Bottom);
            }
        }

        /// <summary>加载设置页配置并同步到各设置区域。</summary>
        private async void SettingForm_Load(object sender, EventArgs e)
        {
            await LoadCachedSettings();
            if (IsDisposed) return;
            await LoadBase();
            if (IsDisposed) return;
            _isLoading = false;
            ApplySideEffects(_config);
            Program.GameMain?.RefreshMayhemOverlaySetting();
            Program.GameMain?.RefreshChampSelectCompanionSetting();

            // 自动启动由 GameMain 的启动生命周期统一执行，避免依赖用户是否打开“设置”标签。
        }

        /// <summary>创建分类设置页面并建立切换关系。</summary>
        private void InitializeSegmentedSettings()
        {
            Controls.Remove(gridPanel2);

            _settingsSegmented.Dock = DockStyle.Left;
            _settingsSegmented.Vertical = true;
            _settingsSegmented.Width = 200 * DeviceDpi / 96;
            _settingsSegmented.Font = new Font(UiMetrics.FontFamily, 11F, FontStyle.Bold);
            _settingsSegmented.Full = false;
            _settingsSegmented.Margin = new Padding(10, 8, 10, 6);
            _settingsSegmented.Items.Add(new AntdUI.SegmentedItem { Text = "对局自动化" });
            _settingsSegmented.Items.Add(new AntdUI.SegmentedItem { Text = "客户端与数据" });
            _settingsSegmented.Items.Add(new AntdUI.SegmentedItem { Text = "外观与窗口" });
            _settingsSegmented.Items.Add(new AntdUI.SegmentedItem { Text = "喊话" });
            _settingsSegmented.Items.Add(new AntdUI.SegmentedItem { Text = "AI 与推荐" });
            _settingsSegmented.Items.Add(new AntdUI.SegmentedItem { Text = "客户端工具" });
            _settingsSegmented.SelectIndexChanged += (_, e) => ShowSettingsSegment(e.Value);

            _settingsSegmentHost.Dock = DockStyle.Fill;
            _settingsSegments[0] = CreateMatchSegment();
            foreach (Control? segment in _settingsSegments)
            {
                if (segment is null) continue;
                segment.Dock = DockStyle.Fill;
                segment.Visible = false;
                _settingsSegmentHost.Controls.Add(segment);
            }

            var root = new AntdUI.Panel { Dock = DockStyle.Fill, Padding = new Padding(12), Radius = 12 };
            root.Controls.Add(_settingsSegmentHost);
            root.Controls.Add(_settingsSegmented);
            Controls.Add(root);
            _settingsSegmented.SelectIndex = 0;
            ShowSettingsSegment(0);
        }

        /// <summary>显示选中的设置分类。</summary>
        private void ShowSettingsSegment(int index)
        {
            if (index < 0 || index >= _settingsSegments.Length) return;
            HideSettingTips();
            EnsureSettingsSegment(index);
            _settingsSegmentHost.SuspendLayout();
            try
            {
                for (int i = 0; i < _settingsSegments.Length; i++)
                    if (_settingsSegments[i] is Control segment) segment.Visible = i == index;
            }
            finally { _settingsSegmentHost.ResumeLayout(true); }
        }

        public void ShowAiRecommendationSettings()
        {
            _settingsSegmented.SelectIndex = 4;
            ShowSettingsSegment(4);
        }

        /// <summary>子页面在首次使用时创建，切换回来复用原有状态。</summary>
        private Control EnsureSettingsSegment(int index)
        {
            if (_settingsSegments[index] is Control existing) return existing;
            using var performance = new UiPerformanceSample("设置子页 " + index);
            Control segment = index switch
            {
                1 => CreateClientSegment(),
                2 => CreateWindowSegment(),
                3 => CreateShoutSegment(),
                4 => CreateAiSegment(),
                5 => new ClientToolsForm(_matchFeatures,
                    AppCompositionRoot.ChampionInsightsService, AppCompositionRoot.ProfileIconService),
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };
            segment.Dock = DockStyle.Fill;
            segment.Visible = false;
            SizeInteractiveControls(segment);
            ResizeSettingColumns(segment);
            UiTheme.Apply(segment);
            _settingsSegments[index] = segment;
            _settingsSegmentHost.Controls.Add(segment);
            return segment;
        }

        /// <summary>切换页面前隐藏当前设置提示。</summary>
        private void HideSettingTips()
        {
            if (_activeFeatureTipControl is { IsDisposed: false } feature)
                _featureTip.Hide(feature);
            if (_activeDesignerTipControl is { IsDisposed: false } designer)
                toolTip1.Hide(designer);
            _activeFeatureTipControl = null;
            _activeDesignerTipControl = null;
        }

        #region 本地缓存

        /// <summary>读取当前缓存的设置数据。</summary>
        private async Task LoadCachedSettings()
        {
            _isLoading = true;
            _config = _settingsStore.Load();

            label_cache_status.Text = $"缓存文件: {_settingsStore.GetStoragePath()}";

            swi_open.Checked = _config.AutoMatch;
            swi_gametrue.Checked = _config.AutoAccept;
            LoadMatchToolsSettings();
            swi_jyyx.Checked = _config.AutoBan;
            swi_xyx.Checked = _config.AutoPick;
            _autoSwapAramBench.Checked = _config.AutoSwapAramBench;
            swi_tray.Checked = _config.MinimizeToTray;
            swi_auto_refresh.Checked = _config.AutoRefresh;
            input_auto_refresh.Value = _config.AutoRefreshIntervalSeconds;
            swi_notify_end.Checked = _config.NotifyOnGameEnd;
            swi_startup.Checked = _config.LaunchOnStartup;

            inputCheckInterval.Value = _config.CheckIntervalSeconds;
            LoadExtendedSettings();

            foreach (var res in ResolutionPresets)
                select_resolution.Items.Add(res);

            int resIndex = Array.IndexOf(ResolutionPresets, _config.Resolution);
            if (resIndex >= 0)
                select_resolution.SelectedIndex = resIndex;

            if (_autoSaveEventsBound) return;
            _autoSaveEventsBound = true;
            // 每个实例只注册一次；初次填值由 _isLoading 抑制。
            input_auto_refresh.ValueChanged += (_, _) => SaveSettings();
            inputCheckInterval.ValueChanged += (_, _) => SaveSettings();
            setting_select_jyx.SelectedValueChanged += (_, _) => { SaveSettings(); _ = UpdateBanPreviewAsync(); };
            setting_select_xyx.SelectedValueChanged += (_, _) => { SaveSettings(); _ = UpdatePickPreviewAsync(); };
            _aramBenchPriority.SelectedValueChanged += (_, _) => SaveSettings();
        }

        /// <summary>将当前设置界面的内容保存到配置存储。</summary>
        private void SaveSettings()
        {
            if (_isLoading) return;

            _config.AutoMatch = swi_open.Checked;
            _config.AutoAccept = swi_gametrue.Checked;
            ReadMatchToolsSettings();
            _config.AutoBan = swi_jyyx.Checked;
            _config.AutoPick = swi_xyx.Checked;
            _config.MinimizeToTray = swi_tray.Checked;
            _config.AutoRefresh = swi_auto_refresh.Checked;
            _config.AutoRefreshIntervalSeconds = (int)input_auto_refresh.Value;
            _config.NotifyOnGameEnd = swi_notify_end.Checked;
            _config.LaunchOnStartup = swi_startup.Checked;
            _config.CheckIntervalSeconds = (int)inputCheckInterval.Value;
            _config.BanChampions = GetSelectedTexts(setting_select_jyx);
            _config.PickChampions = GetSelectedTexts(setting_select_xyx);
            _config.AutoSwapAramBench = _autoSwapAramBench.Checked;
            _config.AramBenchPriorityChampions = GetSelectedTexts(_aramBenchPriority);

            if (select_resolution.SelectedIndex >= 0 && select_resolution.SelectedIndex < ResolutionPresets.Length)
                _config.Resolution = ResolutionPresets[select_resolution.SelectedIndex];

            try
            {
                SaveConfigPreservingSelections();
            }
            catch (Exception ex)
            {
                LOL_GameAssistant.Helper.UiMessage.error(Program.GameMain, $"设置保存失败：{ex.Message}");
                return;
            }
            ApplySideEffects(_config);
            Program.GameMain?.RefreshMayhemOverlaySetting();
            Program.GameMain?.RefreshChampSelectCompanionSetting();
            label_cache_status.Text = $"已缓存: {_settingsStore.GetStoragePath()}";
        }

        // The rune editor and OP.GG picker save through the same store while this settings
        // page remains open. Keep their newer data when an unrelated setting is changed.
        private void SaveConfigPreservingSelections()
        {
            AssistantSettings latest = _settingsStore.Load();
            _config.PersonalRunePresets = latest.PersonalRunePresets;
            _config.LiveGameLayout = latest.LiveGameLayout;
            _config.OpggManualBuildSelections = latest.OpggManualBuildSelections;
            _config.RememberedHeroSkins = latest.RememberedHeroSkins;
            _config.SkinCoreEnabled = latest.SkinCoreEnabled;
            _config.SkinHotkeysEnabled = latest.SkinHotkeysEnabled;
            _config.SkinPickerHotkey = latest.SkinPickerHotkey;
            _config.SkinPreviousHotkey = latest.SkinPreviousHotkey;
            _config.SkinNextHotkey = latest.SkinNextHotkey;
            _settingsStore.Save(_config);
        }

        /// <summary>
        /// 将需要立即生效的设置同步到系统或其它模块。
        /// </summary>
        private static void ApplySideEffects(AssistantSettings config, bool applyStartup = true)
        {
            if (applyStartup) ApplyStartupSetting(config.LaunchOnStartup);
            GameMain? main = Program.GameMain;
            if (main is null || main.IsDisposed) return;

            main.ApplyWindowSettings(config);
            if (Program.GameMain?.liveGameForm is { IsDisposed: false } liveGameForm)
            {
                liveGameForm.ConfigureAutoRefresh(config.AutoRefresh,
                    Math.Max(10, config.AutoRefreshIntervalSeconds));
                liveGameForm.RefreshKdaAnnouncements();
            }
            main.ApplyRecommendationSettings(config);
            if (Program.GameMain?.coachForm is { IsDisposed: false } coachForm)
                coachForm.RefreshOpggAvailability();
        }

        /// <summary>
        /// “通用”分页由设计器生成，这里统一补上悬停说明。
        /// 只挂到该行的标题、开关与选择框本身：英雄头像预览另有一套显示名称的提示，
        /// 递归挂载会把那套提示覆盖掉。
        /// </summary>
        private void AttachCommonSegmentTips()
        {
            AttachTipDeep(labelAutoMatch, swi_open,
                "开启后，客户端回到大厅时助手会自动开始排队匹配；两次自动排队之间至少间隔 10 秒，避免重复触发。");
            AttachTipDeep(labelAutoAccept, swi_gametrue,
                "开启后，匹配到对手时自动点“接受”，不会因为没来得及点而退回队列。");
            AttachTipDeep(label3, swi_jyyx,
                "开启后在选人阶段快速检查本人的禁用动作，并按列表顺序选择可禁用的英雄。");
            AttachTipDeep(setting_select_jyx, "要自动禁用的英雄，可多选，按列表顺序尝试禁用。");
            AttachTip(flow_ban_preview, "已选禁用英雄的头像预览。");
            AttachTipDeep(label4, swi_xyx,
                "开启后在选人阶段快速检查本人的选用动作，并按列表顺序选择可用的英雄。");
            AttachTipDeep(setting_select_xyx, "要自动抢选的英雄，可多选，按列表顺序尝试选用。");
            AttachTip(flow_pick_preview, "已选抢选英雄的头像预览。");
            AttachTipDeep(label_resolution, select_resolution,
                "记录你常用的游戏分辨率，便于按分辨率调整界面与浮窗；当前版本只保存该值，不影响其它功能。");
            AttachTipDeep(label_tray, swi_tray,
                "开启后，关闭窗口只会最小化到系统托盘，单击托盘图标可恢复；关闭则关闭窗口即退出程序。");
            AttachTipDeep(label_auto_refresh, swi_auto_refresh,
                "开启后在对局中按下面的间隔自动刷新对局页数据（玩家战绩、KDA 标签、开黑标记）。");
            AttachTipDeep(label_refresh_interval, input_auto_refresh,
                "对局自动刷新的间隔（秒），最小 10 秒；间隔越短对客户端的请求压力越大。");
            AttachTipDeep(label_notify_end, swi_notify_end,
                "对局结束时用托盘气泡提醒，并写入消息区。");
            AttachTipDeep(label_startup, swi_startup,
                "登录 Windows 后自动启动助手（写入当前用户的启动项）。");
            AttachTipDeep(label_cache, label_cache_status,
                "设置与缓存的存放位置；删除该文件相当于恢复默认设置。本页的开关切换后会自动保存，不需要再点保存按钮。");
        }

        /// <summary>创建匹配及对局自动操作的设置区域。</summary>
        private Control CreateMatchSegment()
            => CreateMatchDashboard();

        /// <summary>向设置布局追加分组标题。</summary>
        private static void AddSectionHeader(TableLayoutPanel layout, int row, string text)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var title = new AntdUI.Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
                Padding = new Padding(0, 16, 0, 7)
            };
            layout.Controls.Add(title, 0, row);
            layout.SetColumnSpan(title, 2);
        }

        /// <summary>创建客户端启动及相关功能设置区域。</summary>
        private Control CreateClientSegment()
        {
            var panel = CreateSegmentPanel();
            var layout = CreateSegmentLayout();
            panel.Controls.Add(layout);

            var browse = new AntdUI.Button { Text = "选择安装文件夹", AutoSize = true, Dock = DockStyle.Right };
            browse.Click += (_, _) => BrowseClientExecutable();
            var pathPanel = new AntdUI.Panel { Dock = DockStyle.Fill, Height = 32 };
            pathPanel.Controls.Add(_clientPath);
            pathPanel.Controls.Add(browse);

            _launchClientButton.Click += async (_, _) => await StartLeagueClientFromSettingsAsync();
            var note = CreateNote("选择 LOL 安装文件夹后，助手会优先使用国服 TCLS/client.exe；其他安装使用 LeagueClient.exe 或 Riot 启动器。首次启动可能需要在启动器完成登录或更新。\n也可留空，由助手从安装清单和常见位置查找。自动启动只在助手打开时执行一次。");

            AddSegmentRow(layout, 0, "安装文件夹：", pathPanel,
                "LOL 安装目录。助手会查找 TCLS/client.exe 或 LeagueClient.exe，用于“立即启动”和启动助手时的自动启动；留空则尝试常见安装位置。");
            AddSegmentRow(layout, 1, "自动启动：", _autoLaunchClient,
                "开启后，每次启动助手时尝试启动 LOL 客户端；国服可能需要先在 Riot/WeGame 启动器登录。只在助手启动时执行一次，保存设置不会重复拉起客户端。");
            AddSegmentRow(layout, 2, "操作：", _launchClientButton,
                "按上面的安装目录立即启动客户端，并等待 LCU 连接；成功后会把实际路径写回上面的输入框。");
            AddSegmentRow(layout, 3, "状态：", _clientStatus,
                "上一次启动与连接的结果。");
            AddSegmentRow(layout, 4, "说明：", note);
            AddSectionHeader(layout, 5, "应用启动与本地数据");
            AddSegmentRow(layout, 6, "开机自启：", swi_startup);
            AddSegmentRow(layout, 7, "本地缓存：", label_cache_status);
            AddSegmentRow(layout, 8, "保存方式：", CreateNote("开关即时保存，安装路径编辑后离开输入框自动保存。"));
            layout.ResumeLayout(true);
            return panel;
        }

        /// <summary>创建窗口行为和热键设置区域。</summary>
        private Control CreateWindowSegment()
        {
            var panel = CreateSegmentPanel();
            var layout = CreateSegmentLayout();
            panel.Controls.Add(layout);

            var opacityPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            opacityPanel.Controls.Add(_opacity);
            opacityPanel.Controls.Add(new AntdUI.Label { Text = "%（40–100）", AutoSize = true, Padding = new Padding(6, 6, 0, 0) });
            _hotkey.KeyDown += CaptureHoldToTopHotkey;
            _hotkey.Click += (_, _) => _hotkey.Focus();
            _hotkey.Enter += (_, _) => Program.GameMain.SetWindowHotkeyCapturePaused(true);
            _hotkey.Leave += (_, _) => Program.GameMain.SetWindowHotkeyCapturePaused(false);
            var holdNote = CreateNote("点击输入框后按一个按键。按住该键时助手会以不抢焦点的方式临时置顶，松开后隐藏到托盘；默认是键盘左上角的 · 键。\nWindows 会接管已注册的按键；快捷键不会注入或修改游戏客户端。");

            AddSegmentRow(layout, 0, "界面主题：", _themeMode,
                "界面配色：跟随系统、浅色或深色，保存后立即生效。");
            AddSegmentRow(layout, 1, "界面语言：", _languageMode, "保存后立即切换简体中文、繁体中文或英文界面。");
            AddSegmentRow(layout, 2, "窗口透明度：", opacityPanel,
                "助手窗口的不透明度（40–100），数值越小越透明。");
            AddSegmentRow(layout, 3, "按住置顶键：", _hotkey,
                "点击输入框后按一个键即可设定。按住该键时助手临时置顶且不抢焦点，松开后隐藏到托盘。");
            AddSegmentRow(layout, 4, "快捷键范围：", _onlyLeagueFocused,
                "开启时只有 LOL 位于前台才响应置顶键；关闭则任何窗口下都响应。");
            AddSegmentRow(layout, 5, "置顶说明：", holdNote);
            AddSectionHeader(layout, 6, "窗口行为");
            AddSegmentRow(layout, 7, "最小化到托盘：", swi_tray);
            AddSegmentRow(layout, 8, "游戏分辨率：", select_resolution);
            AddSegmentRow(layout, 9, "保存方式：", CreateNote("开关、主题、语言、透明度与快捷键修改后自动保存。"));
            layout.ResumeLayout(true);
            return panel;
        }

        /// <summary>创建快捷喊话配置区域。</summary>
        private Control CreateShoutSegment()
        {
            _quickShoutForm = new QuickShoutForm(AppCompositionRoot.QuickShoutService,
                (phrase, sendToAll, useClipboard, perCharacter, interval) =>
                    Program.GameMain.SendQuickShoutToGameAsync(phrase, sendToAll, useClipboard,
                        perCharacter, interval),
                (phrases, sendToAll, useClipboard, perCharacter, interval) =>
                    Program.GameMain.SendQuickShoutBatchToGameAsync(phrases, sendToAll, useClipboard,
                        perCharacter, interval),
                () => Program.GameMain.TestGameChatOpenAsync(),
                SaveShoutSettings, PersistValue);
            _quickShoutForm.LoadSettings(_settingsStore.Load());
            return _quickShoutForm;
        }

        /// <summary>从配置的喊话文案中随机选择并发送到游戏。</summary>
        public Task SendRandomQuickShoutToGameAsync(bool custom) =>
            ((QuickShoutForm)EnsureSettingsSegment(3)).SendRandomToGameAsync(custom);

        /// <summary>将当前选中的喊话文案发送到游戏。</summary>
        public Task SendSelectedQuickShoutToGameAsync() =>
            ((QuickShoutForm)EnsureSettingsSegment(3)).SendSelectedBatchToGameAsync();

        /// <summary>保存快捷喊话界面当前选项。</summary>
        private void SaveShoutSettings()
        {
            if (_isLoading || _quickShoutForm is null) return;
            AssistantSettings latest = _settingsStore.Load();
            _quickShoutForm.WriteSettings(latest);
            if (WindowHoldController.HasGameHotkeyConflict(latest))
                throw new InvalidOperationException("已启用的喊话、KDA 快捷键不能重复或与按住置顶键、对局面板快捷键相同。");
            _settingsStore.Save(latest);
            _config = latest;
            Program.GameMain.ConfigureQuickShoutHotkeys(latest);
        }

        /// <summary>创建 AI 服务、模型及推荐显示设置区域。</summary>
        private Control CreateAiSegment()
        {
            var panel = CreateAiDashboard(out var content);
            _battleOverlayHotkey.KeyDown += (_, e) =>
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                if (!WindowHoldController.TryParseGameHotkey(WindowHoldController.FormatGameHotkey(e.KeyData), out Keys key)) return;
                _battleOverlayHotkey.Tag = key;
                _battleOverlayHotkey.Text = key == Keys.Oem3 ? "·" : WindowHoldController.FormatGameHotkey(key);
                PersistValue(s => s.BattleOverlayHotkey = WindowHoldController.FormatGameHotkey(key));
            };
            _battleOverlayHotkey.Click += (_, _) => _battleOverlayHotkey.Focus();
            _battleOverlayHotkey.Enter += (_, _) => Program.GameMain.SetWindowHotkeyCapturePaused(true);
            _battleOverlayHotkey.Leave += (_, _) => Program.GameMain.SetWindowHotkeyCapturePaused(false);


            _gameKdaAnnouncementEnabled.CheckedChanged += (_, _) =>
            {
                _gameKdaOnePlayerPerLine.Enabled = true;
            };
            _provider.SelectedIndexChanged += (_, _) => ApplyProviderDefaults();
            _apiKey.TextChanged += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(_apiKey.Text)) _clearAiKey = false;
            };

            var providerPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            providerPanel.Controls.Add(_provider);
            var keyPortal = new AntdUI.Button { Text = "获取 API Key", AutoSize = true };
            keyPortal.Click += (_, _) => OpenKeyPortal();
            providerPanel.Controls.Add(keyPortal);
            _testAi.Click += async (_, _) => await TestAiConnectionAsync();
            providerPanel.Controls.Add(_testAi);
            _fetchModels.Click += async (_, _) => await FetchAiModelsAsync();
            providerPanel.Controls.Add(_fetchModels);

            var clearApiKeyButton = new AntdUI.Button { Text = "清除已保存密钥", AutoSize = true, Dock = DockStyle.Right };
            clearApiKeyButton.Click += (_, _) =>
            {
                _clearAiKey = true;
                _apiKey.Clear();
                _apiKeyStatus.Text = "将在保存后清除";
            };
            var keyPanel = new AntdUI.Panel { Dock = DockStyle.Fill, Height = 32 };
            keyPanel.Controls.Add(_apiKey);
            keyPanel.Controls.Add(clearApiKeyButton);

            var refreshPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            refreshPanel.Controls.Add(_dynamicRefresh);
            refreshPanel.Controls.Add(new AntdUI.Label { Text = "兜底分析秒数", AutoSize = true, Padding = new Padding(10, 5, 0, 0) });
            refreshPanel.Controls.Add(_dynamicSeconds);
            var budgetPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            budgetPanel.Controls.Add(new AntdUI.Label { Text = "请求最短间隔（秒）", AutoSize = true });
            budgetPanel.Controls.Add(_aiMinimumSeconds);
            budgetPanel.Controls.Add(new AntdUI.Label { Text = "本局请求上限", AutoSize = true });
            budgetPanel.Controls.Add(_aiBudget);

            _showPopup.Text = "生成 AI 建议后显示提醒";
            var overlayOffsetPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            overlayOffsetPanel.Controls.Add(new AntdUI.Label { Text = "横向", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
            overlayOffsetPanel.Controls.Add(_overlayOffsetX);
            overlayOffsetPanel.Controls.Add(new AntdUI.Label { Text = "纵向", AutoSize = true, Padding = new Padding(8, 5, 0, 0) });
            overlayOffsetPanel.Controls.Add(_overlayOffsetY);
            overlayOffsetPanel.Controls.Add(new AntdUI.Label { Text = "停留秒数", AutoSize = true, Padding = new Padding(8, 5, 0, 0) });
            overlayOffsetPanel.Controls.Add(_overlayDuration);
            var overlayNote = CreateAiNote("浮窗背景完全透明，只显示带深色描边的文字，不遮挡游戏画面；默认显示在游戏左下角，不抢键盘焦点；横向/纵向偏移以所选角落为基准。关闭此项后，建议只会更新到“智能建议”页。 ");
            var privacyNote = CreateAiNote("数据说明：启用并配置服务后，英雄、阶段、阵容、时间、金币、本人生命/法力/技能等级、匿名记分板装备与战绩、近期公开事件、确认的对线与增幅候选会用于 AI 分析。最近三条建议及其反馈随请求发送，用于避免重复；本局完整记录仅保留在内存。不发送玩家名称、账号标识、认证信息、聊天或截图。未配置模型或密钥时只提供本机状态提醒。");
            var opggNote = CreateAiNote("开启后，助手会在选人阶段后台加载推荐，不自动弹出方案窗。点击“一键配置”后查看并应用方案；符文页已满时可选择是否覆盖当前自定义页。另行开启符文自动配置时，按该设置自动应用。");
            var kdaAnnouncementNote = CreateAiNote("选人发送只汇总我方，通过客户端群聊发送一次；对局发送默认蓝方、红方各一条。勾选“单人一行”后，每名玩家各发送一条，标准 5v5 共 10 条，消息会更紧凑。对局发送沿用喊话页的游戏内发送方式与“所有人”选项。两者使用已获取最多 500 条摘要中最近 50 场同模式有效对局；KDA 和胜率分别排除缺失字段。默认满 10 场按累计 KDA 分档，样本下限可在下方调整；不足时标“数据不足”。同模式无有效KDA样本时自动使用全模式参考，并排除自定义对局。若玩家资料暂不可查，会在消息中标注。选人模板支持 {players}、{allies}。");

            var ratingPolicyPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            ratingPolicyPanel.Controls.Add(_ratingMinimumGames);
            var selection = CreateAiSettingsCard(content, "aiSelectionCard", "选人辅助与符文");
            AddSegmentRow(selection, 1, "OP.GG 推荐：", _opggBuildAssistantEnabled,
                "选人阶段检测到已选英雄后后台加载 OP.GG 推荐；点击“一键配置”查看方案，不自动弹窗。");
            AddSegmentRow(selection, 2, "符文自动配置：", _autoApplyRuneBuild,
                "按英雄、模式和分路优先选择已启用的个人方案；没有匹配方案时自动应用 OP.GG 推荐。仅选人阶段执行一次，手动选择方案不受影响。");
            AddSegmentRow(selection, 3, "选人伴随窗：", _champSelectCompanionEnabled,
                "选人时跟随客户端的位置、大小和最小化状态，仅展示可见队友的最近表现与自动选禁状态。");
            AddSegmentRow(selection, 4, "OP.GG 说明：", opggNote);
            selection.ResumeLayout(true);

            var battle = CreateAiSettingsCard(content, "aiBattleCard", "局内对局信息面板");
            AddSegmentRow(battle, 1, "对局信息：", _battleOverlayEnabled);
            AddSegmentRow(battle, 2, "面板快捷键：", _battleOverlayHotkey,
                "默认 · 键，可设置单键或 Ctrl / Alt / Shift 组合键。按住显示，松开或切出游戏后隐藏。");
            AddSegmentRow(battle, 3, "面板说明：", CreateAiNote("游戏位于前台时，面板快捷键显示独立的蓝红双方信息表，不抢焦点。\n启用后，游戏中使用面板快捷键；客户端或桌面仍使用“外观与窗口”中的按住置顶键。\n可查看段位、赛季战绩、近期胜负、英雄样本胜率和本局 KDA / 补刀 / 视野得分。窗口或无边框模式下使用。"));
            battle.ResumeLayout(true);

            var mayhem = CreateAiSettingsCard(content, "aiMayhemCard", "海克斯增幅助手");
            AddSegmentRow(mayhem, 1, "海克斯侧边栏：", _mayhemOverlayEnabled,
                "仅海克斯大乱斗的窗口或无边框模式显示；独占全屏自动隐藏并停止扫描。可识别增幅卡片并比较胜率。");
            mayhem.ResumeLayout(true);

            var performance = CreateAiSettingsCard(content, "aiPerformanceCard", "战绩评估与 KDA 发送");
            AddSegmentRow(performance, 1, "评级样本下限：", ratingPolicyPanel,
                "首页、对局、伴随窗和浮窗共用；默认至少10场同模式有效KDA才评级。无同模式有效KDA时自动统计全模式，排除自定义。");
            AddSegmentRow(performance, 2, "选人 KDA 发送：", _champSelectKdaAnnouncementEnabled,
                "选人阶段我方战绩加载完成后，把近期 KDA 评估通过客户端聊天发送一次；只汇总我方玩家。");
            AddSegmentRow(performance, 3, "发送文案：", _champSelectKdaAnnouncementTemplate,
                "发送用的模板，支持 {players} 与 {allies}（两者内容相同，都是我方名单）。");
            AddSegmentRow(performance, 4, "对局 KDA 发送：", _gameKdaAnnouncementEnabled,
                "游戏进行中双方玩家的近期 KDA 评估加载完成后，按喊话页设置发送到游戏聊天，一局一次；会包含敌方玩家。也可在“一键喊话”设置中配置手动 KDA 快捷键（默认 F9），关闭自动发送后仍能手动发送。");
            AddSegmentRow(performance, 5, "对局发送排版：", _gameKdaOnePlayerPerLine,
                "勾选后每名玩家各发一条，游戏聊天中每人占一行；不勾选时蓝方、红方各发一条。同时用于自动发送和快捷键手动发送。");
            AddSegmentRow(performance, 6, "KDA 说明：", kdaAnnouncementNote);
            performance.ResumeLayout(true);

            var connection = CreateAiSettingsCard(content, "aiConnectionCard", "AI 服务连接");
            AddSegmentRow(connection, 1, "服务商：", providerPanel,
                "这一行有三个按钮：“获取 API Key”打开服务商密钥页；“获取可用模型”用当前密钥读取服务端支持的模型名；“测试连接”用当前填写的服务商、模型与密钥发一次最小请求。");
            AddSegmentRow(connection, 2, "模型名称：", _model,
                "要调用的模型名，建议先点“获取可用模型”再从这里选。模型名由服务商决定，写错时只会得到 400。");
            AddSegmentRow(connection, 3, "接口地址：", _baseUrl,
                "API 基础地址；远端必须使用 HTTPS。仅 localhost 或 127.0.0.1 可使用未加密 HTTP，请只连接可信本机服务。");
            AddSegmentRow(connection, 4, "API Key：", keyPanel,
                "填写后保存即可。已保存的密钥不会回显，留空保存表示保留原密钥；“清除已保存密钥”会在下次保存时删除它。");
            AddSegmentRow(connection, 5, "密钥状态：", _apiKeyStatus,
                "当前密钥的保存状态；密钥使用 Windows 用户级加密存放。");
            AddSegmentRow(connection, 6, "连通性：", _aiTestStatus,
                "“测试连接”的结果，会显示服务端返回的真实原因，例如模型名称不存在、Key 无效或余额不足。");
            AddSaveRow(connection, 7, "保存 AI 连接与密钥");
            AddSegmentRow(connection, 8, "保存方式：", CreateAiNote("所有开关立即保存；服务商、地址、模型与密钥填写完成后使用上方按钮保存。"));
            connection.ResumeLayout(true);

            var analysis = CreateAiSettingsCard(content, "aiAnalysisCard", "AI 时间线与分析策略");
            AddSegmentRow(analysis, 1, "AI 时间线：", _recommendationEnabled,
                "开启后采集本机对局事实，提供即时状态提醒与 AI 行动计划；关闭则停止采集和请求。");
            AddSegmentRow(analysis, 2, "动态建议：", refreshPanel,
                "装备、阵容等变化触发分析；无明显变化时按此间隔兜底。关闭后仅阶段切换和手动操作发起分析，仍同步本机数据并撤下失效建议。");
            AddSegmentRow(analysis, 3, "请求预算：", budgetPanel, "选人与局内共享预算，失败请求也计入；手动分析至少间隔8秒。新一局自动重置。");
            AddSegmentRow(analysis, 4, "建议质量：", CreateAiNote("行动计划必须包含具体步骤、事实依据、执行条件与备选方案。未通过校验的输出会被拦截；没有可靠新行动时继续观察。低血状态由本机即时提醒。"));
            AddSegmentRow(analysis, 5, "数据与隐私：", privacyNote);
            analysis.ResumeLayout(true);

            var display = CreateAiSettingsCard(content, "aiDisplayCard", "AI 提醒与浮窗");
            AddSegmentRow(display, 1, "建议提醒：", _showPopup,
                "生成新建议后弹出提醒。与“游戏内浮窗”共用同一个显示，两者任一开启就会弹出浮窗。");
            AddSegmentRow(display, 2, "游戏内浮窗：", _overlayEnabled,
                "在游戏内显示建议浮窗，背景透明只显示文字，不抢键盘焦点；关闭后建议只会更新到“智能建议”页。");
            AddSegmentRow(display, 3, "浮窗位置：", _overlayPosition,
                "浮窗停靠的屏幕角落。");
            AddSegmentRow(display, 4, "位置与时长：", overlayOffsetPanel,
                "横向/纵向偏移以所选角落为基准；停留秒数是浮窗自动隐藏前的显示时长。");
            AddSegmentRow(display, 5, "浮窗说明：", overlayNote);
            display.ResumeLayout(true);

            CreateCooldownPreviewCard(content);

            content.ResumeLayout(true);
            return panel;
        }

        // AntdUI.Panel 自身不提供滚动条；此处仅作为滚动内容承载容器，内部控件仍使用 AntdUI。
        private static AntdUI.Panel CreateSegmentPanel() => new()
        {
            AutoScroll = true,
            Padding = new Padding(UiMetrics.SpaceMedium)
        };

        /// <summary>创建统一间距的设置分区布局。</summary>
        private static TableLayoutPanel CreateSegmentLayout()
        {
            var layout = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                Padding = new Padding(UiMetrics.SpaceMedium),
                ColumnCount = 2
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            // 批量加入设置行后再统一布局，避免每增加一个控件都重排整个表格。
            layout.SuspendLayout();
            return layout;
        }

        /// <summary>创建设置分区中的说明文本。</summary>
        private static AntdUI.Label CreateNote(string text) => new()
        {
            AutoSize = true,
            MaximumSize = new Size(620, 0),
            ForeColor = Color.DimGray,
            Text = text
        };

        /// <summary>设置项的悬停说明：标题和控件上都挂同一段文字，鼠标停在行的任意位置都能看到。</summary>
        private void AddSegmentRow(TableLayoutPanel layout, int row, string caption, Control control, string? tip = null)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var label = new AntdUI.Label
            {
                Text = caption,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, 7, 0, 0)
            };
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            control.Margin = new Padding(3, 5, 3, 5);
            // Designer controls were formerly docked in fixed-height rows. After moving them
            // into an auto-sized section their measured height can be zero unless restored.
            if (control.Height == 0)
                control.Height = control is AntdUI.SelectMultiple ? 48 : UiMetrics.ControlHeight;
            if (control is AntdUI.Switch)
            {
                control.Anchor = AnchorStyles.Left;
                control.Width = 80;
            }
            else if (control is AntdUI.InputNumber)
            {
                control.Anchor = AnchorStyles.Left;
                control.Width = 120;
            }
            if (!string.IsNullOrWhiteSpace(tip)) AttachTipDeep(label, control, tip);
            layout.Controls.Add(label, 0, row);
            layout.Controls.Add(control, 1, row);
            control.Visible = true;
        }

        /// <summary>只给控件本身挂说明（用于内部已有自定义提示的控件，避免覆盖它的提示）。</summary>
        private void AttachTip(Control control, string text)
        {
            _featureTip.SetToolTip(control, text);
            if (_featureTipTargets.Add(control))
                control.MouseLeave += (_, _) =>
                {
                    if (IsDisposed || control.IsDisposed) return;
                    _featureTip.Hide(control);
                    if (ReferenceEquals(_activeFeatureTipControl, control)) _activeFeatureTipControl = null;
                };
        }

        /// <summary>
        /// 给控件及其所有子控件挂同一段说明。设置了说明的控件多为容器（面板、下拉框、按钮组），
        /// 只有递归下去，鼠标落在里面真正的输入控件上才会显示。
        /// </summary>
        private void AttachTipDeep(Control control, string text)
        {
            AttachTip(control, text);
            foreach (Control child in control.Controls) AttachTipDeep(child, text);
        }

        /// <summary>将同一功能说明绑定到控件及其子控件。</summary>
        private void AttachTipDeep(Control caption, Control control, string text)
        {
            AttachTip(caption, text);
            AttachTipDeep(control, text);
        }

        /// <summary>向设置区域追加保存操作行。</summary>
        private void AddSaveRow(TableLayoutPanel layout, int row, string text)
        {
            var save = new AntdUI.Button { Text = text, AutoSize = true };
            save.Click += (_, _) => SaveExtendedSettings();
            AddSegmentRow(layout, row, "", save, $"点击后立即生效，并写入本机的 {_settingsStore.GetStoragePath()}");
        }

        /// <summary>读取扩展设置并同步到相应控件。</summary>
        private void LoadExtendedSettings()
        {
            _config.Normalize();
            _clientPath.Text = _gameClientLauncher.NormalizeConfiguredDirectory(_config.GameClientPath);
            _autoLaunchClient.Checked = _config.AutoLaunchGameClient;
            _opacity.Value = _config.WindowOpacityPercent;
            _themeMode.SelectedIndex = _config.ThemeMode switch { "Light" => 1, "Dark" => 2, _ => 0 };
            _languageMode.SelectedIndex = _config.LanguageMode switch { "en-US" => 1, "zh-TW" => 2, _ => 0 };
            Keys holdKey = WindowHoldController.ParseKey(_config.HoldToTopHotkey);
            _hotkey.Tag = holdKey;
            _hotkey.Text = WindowHoldController.DescribeKey(holdKey);
            _battleOverlayEnabled.Checked = _config.BattleOverlayEnabled;
            Keys overlayKey = WindowHoldController.TryParseGameHotkey(_config.BattleOverlayHotkey, out Keys parsedOverlay) ? parsedOverlay : Keys.Oem3;
            _battleOverlayHotkey.Tag = overlayKey;
            _battleOverlayHotkey.Text = overlayKey == Keys.Oem3 ? "·" : WindowHoldController.FormatGameHotkey(overlayKey);
            _onlyLeagueFocused.Checked = _config.HoldToTopOnlyWhenLeagueFocused;
            _quickShoutForm?.LoadSettings(_config);

            CloudAiSettings ai = _config.Ai;
            _recommendationEnabled.Checked = ai.RecommendationEnabled;
            _opggBuildAssistantEnabled.Checked = _config.OpggBuildAssistantEnabled;
            _autoApplyRuneBuild.Checked = _config.AutoApplyRuneBuild;
            _mayhemOverlayEnabled.Checked = _config.MayhemOverlayEnabled;
            _champSelectCompanionEnabled.Checked = _config.ChampSelectCompanionEnabled;
            _champSelectKdaAnnouncementEnabled.Checked = _config.ChampSelectKdaAnnouncementEnabled;
            _gameKdaAnnouncementEnabled.Checked = _config.GameKdaAnnouncementEnabled;
            _gameKdaOnePlayerPerLine.Checked = _config.GameKdaOnePlayerPerLine;
            _ratingMinimumGames.Value = Math.Clamp(_config.RecentPerformanceMinimumGames, 1, 50);
            _gameKdaOnePlayerPerLine.Enabled = true;
            _champSelectKdaAnnouncementTemplate.Text = _config.ChampSelectKdaAnnouncementTemplate;
            _provider.SelectedValue = ai.Provider;
            if (_provider.SelectedIndex < 0) _provider.SelectedValue = LOL_GameAssistant.Domain.Settings.AiProvider.OpenAI;
            _model.Text = ai.Model;
            _baseUrl.Text = ai.GetBaseUrl();
            _dynamicRefresh.Checked = ai.DynamicRefreshEnabled;
            _dynamicSeconds.Value = ai.DynamicRefreshSeconds;
            _aiMinimumSeconds.Value = ai.MinimumRequestSeconds;
            _aiBudget.Value = ai.MatchRequestBudget;
            _showPopup.Checked = ai.ShowRecommendationPopup;
            _overlayEnabled.Checked = ai.RecommendationOverlayEnabled;
            _overlayPosition.SelectedIndex = OverlayPositionToIndex(ai.RecommendationOverlayPosition);
            _overlayOffsetX.Value = ai.RecommendationOverlayOffsetX;
            _overlayOffsetY.Value = ai.RecommendationOverlayOffsetY;
            _overlayDuration.Value = ai.RecommendationOverlayDurationSeconds;
            _apiKeyStatus.Text = string.IsNullOrWhiteSpace(ai.EncryptedApiKey) ? "未保存" : "已加密保存在当前 Windows 用户下";
        }

        /// <summary>只提交填写完成的 AI 连接与密钥，其他设置通过各自的自动保存路径处理。</summary>
        private void SaveExtendedSettings()
        {
            if (_isLoading) return;

            AssistantSettings latest = _settingsStore.Load();
            CloudAiSettings ai = latest.Ai;
            ai.Provider = _provider.SelectedValue is LOL_GameAssistant.Domain.Settings.AiProvider provider
                ? provider
                : LOL_GameAssistant.Domain.Settings.AiProvider.OpenAI;
            ai.Model = _model.Text.Trim();
            ai.BaseUrl = _baseUrl.Text.Trim().TrimEnd('/');
            if (!ai.TryGetSafeBaseUri(out Uri? aiUri, out string addressError))
            {
                LOL_GameAssistant.Helper.UiMessage.error(Program.GameMain, addressError);
                return;
            }
            try
            {
                if (_clearAiKey) ai.EncryptedApiKey = "";
                else if (!string.IsNullOrWhiteSpace(_apiKey.Text)) ai.EncryptedApiKey = _settingsSecretProtector.Protect(_apiKey.Text);
                latest.Normalize();
                _settingsStore.Save(latest);
                _config = latest;
            }
            catch (Exception ex)
            {
                LOL_GameAssistant.Helper.UiMessage.error(Program.GameMain, $"设置保存失败：{ex.Message}");
                return;
            }
            ApplySideEffects(_config, applyStartup: false);
            label_cache_status.Text = $"已缓存: {_settingsStore.GetStoragePath()}";
            _apiKey.Clear();
            Program.GameMain?.RefreshChampSelectCompanionSetting();
            _clearAiKey = false;
            _apiKeyStatus.Text = string.IsNullOrWhiteSpace(ai.EncryptedApiKey) ? "未保存" : "已加密保存在当前 Windows 用户下";
            if (Program.GameMain is { } mainWindow) LOL_GameAssistant.Helper.UiMessage.success(mainWindow,
                aiUri!.Scheme == Uri.UriSchemeHttp
                    ? "设置已保存。本机 HTTP 连接未加密，请只连接可信服务。"
                    : "设置已保存");
        }

        /// <summary>打开文件选择器，选取客户端启动程序。</summary>
        private void BrowseClientExecutable()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "选择 LOL 安装文件夹（程序会自动查找国服 TCLS 或 LeagueClient）",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };
            if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
            {
                _clientPath.Text = dialog.SelectedPath;
                PersistValue(s => s.GameClientPath = _gameClientLauncher.NormalizeConfiguredDirectory(_clientPath.Text));
            }
        }

        /// <summary>根据设置中的路径启动客户端并展示启动结果。</summary>
        private async Task StartLeagueClientFromSettingsAsync()
        {
            GameClientLaunchResult result;
            _launchClientButton.Enabled = false;
            _clientStatus.ForeColor = Color.DimGray;
            _clientStatus.Text = "正在查找安装目录并启动客户端，请稍候…";
            try
            {
                result = await _gameClientLauncher.StartAndVerifyAsync(_clientPath.Text.Trim(), _lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                result = new GameClientLaunchResult(false, $"启动验证失败：{ex.Message}");
            }
            finally
            {
                if (!IsDisposed) _launchClientButton.Enabled = true;
            }
            if (IsDisposed) return;
            SetClientStatus(result);
            if (result.Started && !string.IsNullOrWhiteSpace(result.ExecutablePath))
            {
                _clientPath.Text = _gameClientLauncher.NormalizeConfiguredDirectory(result.ExecutablePath);
                _config.GameClientPath = _clientPath.Text;
                AssistantSettings latest = _settingsStore.Load();
                latest.GameClientPath = _clientPath.Text;
                _settingsStore.Save(latest);
            }

            if (result.IsReady) LOL_GameAssistant.Helper.UiMessage.success(Program.GameMain, result.Message);
            else if (result.Started) LOL_GameAssistant.Helper.UiMessage.info(Program.GameMain, result.Message);
            else LOL_GameAssistant.Helper.UiMessage.error(Program.GameMain, result.Message);
        }

        /// <summary>录制按住置顶功能使用的热键。</summary>
        private void CaptureHoldToTopHotkey(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu) return;
            _hotkey.Tag = e.KeyCode;
            _hotkey.Text = WindowHoldController.DescribeKey(e.KeyCode);
            PersistValue(s => s.HoldToTopHotkey = e.KeyCode.ToString());
            e.SuppressKeyPress = true;
            e.Handled = true;
        }

        /// <summary>将浮窗位置配置转换为界面选项索引。</summary>
        private static int OverlayPositionToIndex(string? value) => value switch
        {
            "TopLeft" => 1,
            "BottomRight" => 2,
            "TopRight" => 3,
            "Center" => 4,
            _ => 0
        };

        /// <summary>将界面选择索引转换为浮窗位置配置。</summary>
        private static string IndexToOverlayPosition(int index) => index switch
        {
            1 => "TopLeft",
            2 => "BottomRight",
            3 => "TopRight",
            4 => "Center",
            _ => "BottomLeft"
        };

        /// <summary>服务提供商变化后填入其默认地址和模型配置。</summary>
        private void ApplyProviderDefaults()
        {
            if (_isLoading || _provider.SelectedValue is not LOL_GameAssistant.Domain.Settings.AiProvider provider) return;
            _baseUrl.Text = new CloudAiSettings { Provider = provider }.GetBaseUrl();
            // 下拉里的模型是上一个服务商读回来的，换服务商后不再适用；
            // 已填的模型名保留，用户按“获取可用模型”重新读一次即可。
            string keepModel = _model.Text;
            _model.Items.Clear();
            _model.Text = keepModel;
        }

        /// <summary>
        /// 用界面上当前填写的服务商、模型与密钥发一次最小请求。
        /// 未保存也能先验证，避免“先保存再发现填错”的来回。
        /// </summary>
        private async Task TestAiConnectionAsync()
        {
            _testAi.Enabled = false;
            SetAiTestStatus("正在测试连接…", Color.DimGray);
            try
            {
                string reply = await AppCompositionRoot.AiRecommendationProvider.TestAsync(BuildAiSettingsFromUi(), _lifetime.Token);
                SetAiTestStatus($"连接正常，模型回复：{reply}", Color.ForestGreen);
            }
            catch (Exception ex)
            {
                SetAiTestStatus(ex.Message, Color.Firebrick);
            }
            finally
            {
                if (!IsDisposed) _testAi.Enabled = true;
            }
        }

        /// <summary>
        /// 读取服务商当前可用的模型列表并填进下拉。
        /// 不在代码里内置模型名：模型阵容会变，写死的候选值反而会把用户带偏
        /// （DeepSeek 端点上就已经不再认 deepseek-chat）。
        /// </summary>
        private async Task FetchAiModelsAsync()
        {
            _fetchModels.Enabled = false;
            SetAiTestStatus("正在读取可用模型…", Color.DimGray);
            try
            {
                IReadOnlyList<string> models = await AppCompositionRoot.AiRecommendationProvider.ListModelsAsync(BuildAiSettingsFromUi(), _lifetime.Token);
                if (IsDisposed) return;
                if (models.Count == 0)
                {
                    SetAiTestStatus("服务没有返回任何模型。", Color.DarkGoldenrod);
                    return;
                }

                string current = _model.Text.Trim();
                _model.BeginUpdate();
                try
                {
                    _model.Items.Clear();
                    foreach (string model in models) _model.Items.Add(model);
                }
                finally
                {
                    _model.EndUpdate();
                }

                // 当前填的模型不在服务端列表里（写错或已下线）时，直接换成列表里的第一个。
                bool currentIsValid = models.Any(item => string.Equals(item, current, StringComparison.OrdinalIgnoreCase));
                _model.Text = currentIsValid ? current : models[0];
                SetAiTestStatus($"已读取 {models.Count} 个可用模型，当前选择：{_model.Text}", Color.ForestGreen);
            }
            catch (Exception ex)
            {
                SetAiTestStatus(ex.Message, Color.Firebrick);
            }
            finally
            {
                if (!IsDisposed) _fetchModels.Enabled = true;
            }
        }

        /// <summary>更新 AI 服务测试的状态提示。</summary>
        private void SetAiTestStatus(string text, Color color)
        {
            if (IsDisposed) return;
            _aiTestStatus.ForeColor = color;
            _aiTestStatus.Text = text;
        }

        /// <summary>按界面上当前填写的值组装一份设置，供“测试连接”和“获取可用模型”共用。</summary>
        private CloudAiSettings BuildAiSettingsFromUi() => new()
        {
            Provider = _provider.SelectedValue is LOL_GameAssistant.Domain.Settings.AiProvider provider
                ? provider
                : LOL_GameAssistant.Domain.Settings.AiProvider.OpenAI,
            Model = _model.Text.Trim(),
            BaseUrl = _baseUrl.Text.Trim().TrimEnd('/'),
            // 点了“清除已保存密钥”之后按清除后的状态测，否则测的还是旧密钥。
            EncryptedApiKey = _clearAiKey
                ? ""
                : !string.IsNullOrWhiteSpace(_apiKey.Text)
                    ? _settingsSecretProtector.Protect(_apiKey.Text)
                    : _config.Ai.EncryptedApiKey
        };

        /// <summary>打开当前 AI 服务提供商的密钥管理页面。</summary>
        private void OpenKeyPortal()
        {
            if (_provider.SelectedValue is not LOL_GameAssistant.Domain.Settings.AiProvider provider) return;
            string url = new CloudAiSettings { Provider = provider }.GetKeyPortalUrl();
            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show(FindForm(), "自定义兼容服务请使用该服务商提供的控制台。", "AI 设置", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), $"无法打开浏览器：{ex.Message}", "AI 设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>更新客户端启动或连接的状态提示。</summary>
        private void SetClientStatus(GameClientLaunchResult result)
        {
            _clientStatus.ForeColor = result.IsReady ? Color.ForestGreen
                : result.Started ? Color.DarkGoldenrod
                : Color.Firebrick;
            _clientStatus.Text = result.Message;
        }

        /// <summary>
        /// 设置/取消开机自启（写入 HKCU Run 注册表）。
        /// </summary>
        internal static void ApplyStartupSetting(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true)
                    ?? Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                if (key == null) return;

                if (enabled)
                    key.SetValue("LOLGameAssistant", $"\"{System.Windows.Forms.Application.ExecutablePath}\"");
                else
                    key.DeleteValue("LOLGameAssistant", false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"设置开机自启失败: {ex.Message}");
            }
        }

        /// <summary>提取多选控件中选中条目的文本。</summary>
        private static List<string> GetSelectedTexts(AntdUI.SelectMultiple select)
        {
            var result = new List<string>();
            try
            {
                foreach (var item in select.SelectedValue ?? Array.Empty<object>())
                {
                    string? text = item?.ToString();
                    if (!string.IsNullOrWhiteSpace(text)) result.Add(text);
                }
            }
            catch { }
            return result;
        }

        /// <summary>根据配置文本恢复多选控件的选择。</summary>
        private static void SetSelectedTexts(AntdUI.SelectMultiple select, List<string> texts)
        {
            if (texts.Count == 0) return;
            try
            {
                select.SelectedValue = texts.ToArray();
            }
            catch { }
        }

        /// <summary>响应分辨率设置变化并保存对应配置。</summary>
        private void SelectResolutionChanged(object? sender, EventArgs e)
        {
            if (select_resolution.SelectedIndex < 0 || select_resolution.SelectedIndex >= ResolutionPresets.Length)
                return;

            string selectedRes = ResolutionPresets[select_resolution.SelectedIndex];
            _config.Resolution = selectedRes;
            SaveSettings();

            var parts = selectedRes.Split('x');
            if (parts.Length == 2 && int.TryParse(parts[0], out int width) && int.TryParse(parts[1], out int height))
            {
                var mainForm = Program.GameMain;
                if (mainForm != null && !mainForm.IsDisposed)
                {
                    mainForm.Invoke(() =>
                    {
                        mainForm.ClientSize = new Size(width, height);
                        label_cache_status.Text = $"分辨率已切换到: {selectedRes}";
                    });
                }
            }
        }

        private readonly Dictionary<FlowLayoutPanel, int> _previewRevisions = new();

        /// <summary>
        /// 从共享字节缓存创建当前控件独占的图片。
        /// </summary>
        private async Task<Image?> GetChampionIconAsync(int championId)
        {
            try
            {
                var asset = await AppCompositionRoot.GameAssetService.GetChampionIconAsync(championId, _lifetime.Token);
                return DecodePreviewImage(asset?.Content);
            }
            catch { return null; }
        }

        internal static Image? DecodePreviewImage(byte[]? bytes)
        {
            if (bytes is not { Length: > 0 }) return null;
            using var stream = new MemoryStream(bytes);
            using var source = Image.FromStream(stream);
            return new Bitmap(source);
        }

        /// <summary>
        /// 更新禁用英雄头像预览
        /// </summary>
        private async Task UpdateBanPreviewAsync()
        {
            await UpdatePreviewPanelAsync(flow_ban_preview, setting_select_jyx, GetBanChampionIds());
        }

        /// <summary>
        /// 更新选用英雄头像预览
        /// </summary>
        private async Task UpdatePickPreviewAsync()
        {
            await UpdatePreviewPanelAsync(flow_pick_preview, setting_select_xyx, GetPickChampionIds());
        }

        /// <summary>
        /// 通用预览面板更新
        /// </summary>
        private async Task UpdatePreviewPanelAsync(FlowLayoutPanel panel, AntdUI.SelectMultiple select, List<int> championIds)
        {
            int revision = _previewRevisions.GetValueOrDefault(panel) + 1;
            _previewRevisions[panel] = revision;
            ControlLifetime.ClearAndDispose(panel);
            if (championIds.Count == 0) return;

            var tasks = championIds.Select(async id =>
            {
                var img = await GetChampionIconAsync(id);
                return (id, img);
            }).ToList();

            var results = await Task.WhenAll(tasks);
            if (IsDisposed || panel.IsDisposed || _previewRevisions.GetValueOrDefault(panel) != revision)
            {
                foreach (var (_, image) in results) image?.Dispose();
                return;
            }
            foreach (var (id, img) in results)
            {
                if (img == null) continue;
                var name = AppCompositionRoot.ChampionCatalog.GetDisplayName(id);
                if (string.IsNullOrWhiteSpace(name)) name = "?";
                var pic = new PictureBox
                {
                    Image = img,
                    Size = new Size(32, 32),
                    SizeMode = PictureBoxSizeMode.StretchImage,
                    Margin = new Padding(2)
                };
                pic.Disposed += (_, _) => pic.Image?.Dispose();
                toolTip1.SetToolTip(pic, name);
                panel.Controls.Add(pic);
            }
        }

        #region 外部调用

        /// <summary>
        /// 是否启用自动禁用英雄
        /// </summary>
        public bool IsAutoBanEnabled => swi_jyyx.Checked;

        /// <summary>
        /// 是否启用自动选用英雄
        /// </summary>
        public bool IsAutoPickEnabled => swi_xyx.Checked;

        /// <summary>
        /// 是否启用最小化到系统托盘
        /// </summary>
        public bool IsMinimizeToTray => swi_tray.Checked;

        /// <summary>
        /// 是否启用对局数据自动刷新。
        /// </summary>
        public bool IsAutoRefreshEnabled => swi_auto_refresh.Checked;

        /// <summary>
        /// 对局数据自动刷新间隔（秒）。
        /// </summary>
        public int AutoRefreshIntervalSeconds => Math.Max(10, (int)input_auto_refresh.Value);

        /// <summary>
        /// 获取设置中选定的禁用英雄ID列表
        /// </summary>
        public List<int> GetBanChampionIds()
        {
            return ResolveChampionIds(_config.BanChampions);
        }

        /// <summary>
        /// 获取设置中选定的选用英雄ID列表
        /// </summary>
        public List<int> GetPickChampionIds()
        {
            return ResolveChampionIds(_config.PickChampions);
        }

        #endregion 外部调用

        #endregion 定时执行方法

        /// <summary>加载基础配置并同步到设置控件。</summary>
        private async Task LoadBase()
        {
            var names = AppCompositionRoot.ChampionCatalog.GetAll()
                .Select(champion => (object)champion.DisplayName).ToArray();
            setting_select_jyx.Items.AddRange(names);
            setting_select_xyx.Items.AddRange(names);
            _aramBenchPriority.Items.AddRange(names);
            RestoreSelectedChampions();
        }

        /// <summary>将保存的英雄名称通过应用目录转换为稳定的英雄 ID。</summary>
        private static List<int> ResolveChampionIds(IEnumerable<string> championNames) =>
            championNames
                .Select(AppCompositionRoot.ChampionCatalog.FindIdByDisplayName)
                .OfType<int>()
                .ToList();

        /// <summary>根据已保存的英雄配置恢复选择状态。</summary>
        private void RestoreSelectedChampions()
        {
            SetSelectedTexts(setting_select_jyx, _config.BanChampions);
            SetSelectedTexts(setting_select_xyx, _config.PickChampions);
            SetSelectedTexts(_aramBenchPriority, _config.AramBenchPriorityChampions);
        }
    }
}
