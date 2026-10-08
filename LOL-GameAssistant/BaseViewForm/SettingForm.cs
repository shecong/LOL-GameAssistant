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
    public partial class SettingForm : UserControl
    {
        public DateTime? lastOpenGameTime = null;
        private AssistantSettings _config;
        private bool _isLoading;
        private ToolTip toolTip1 = new ToolTip();

        private readonly ToolTip _featureTip = new()
        {
            // 说明文本较长，默认 5 秒往往读不完。
            AutoPopDelay = 20000,
            InitialDelay = 350,
            ReshowDelay = 100,
            ShowAlways = false
        };
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
            string mode = _languageMode.SelectedIndex == 1 ? "en-US" : "zh-CN";
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
            _config = new AssistantSettings();
            _featureTip.Popup += (_, e) => _activeFeatureTipControl = e.AssociatedControl;
            toolTip1.Popup += (_, e) => _activeDesignerTipControl = e.AssociatedControl;
            VisibleChanged += (_, _) => { if (!Visible) HideSettingTips(); };
            MouseLeave += (_, _) => HideSettingTips();
            AttachCommonSegmentTips();
            InitializeSegmentedSettings();
            Disposed += (_, _) => { toolTip1.Dispose(); _featureTip.Dispose(); };
        }

        /// <summary>加载设置页配置并同步到各设置区域。</summary>
        private async void SettingForm_Load(object sender, EventArgs e)
        {
            await LoadCachedSettings();
            await LoadBase();
            _isLoading = false;
            _languageMode.SelectedIndexChanged += (_, _) => SwitchLanguage();
            ApplySideEffects(_config);
            Program.GameMain?.RefreshMayhemOverlaySetting();
            Program.GameMain?.RefreshChampSelectCompanionSetting();

            // 自动启动由 GameMain 的启动生命周期统一执行，避免依赖用户是否打开“设置”标签。
        }

        /// <summary>创建分类设置页面并建立切换关系。</summary>
        private void InitializeSegmentedSettings()
        {
            Controls.Remove(gridPanel2);

            _settingsSegmented.Dock = DockStyle.Top;
            _settingsSegmented.Height = 42;
            _settingsSegmented.Full = true;
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
            _settingsSegments[1] = CreateClientSegment();
            _settingsSegments[2] = CreateWindowSegment();
            _settingsSegments[3] = CreateShoutSegment();
            _settingsSegments[4] = CreateAiSegment();
            _settingsSegments[5] = new ClientToolsForm(
                AppCompositionRoot.ClientFeatureService,
                AppCompositionRoot.ChampionInsightsService,
                AppCompositionRoot.ProfileIconService);
            foreach (Control segment in _settingsSegments)
            {
                segment.Dock = DockStyle.Fill;
                segment.Visible = false;
                _settingsSegmentHost.Controls.Add(segment);
            }

            var root = new AntdUI.Panel { Dock = DockStyle.Fill };
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
            for (int i = 0; i < _settingsSegments.Length; i++)
                _settingsSegments[i].Visible = i == index;
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

            // 绑定自动保存事件（在初始值设置完成之后绑定，避免首次加载触发保存）
            swi_open.CheckedChanged += (_, _) => SaveSettings();
            swi_gametrue.CheckedChanged += (_, _) => SaveSettings();
            swi_jyyx.CheckedChanged += (_, _) => SaveSettings();
            swi_xyx.CheckedChanged += (_, _) => SaveSettings();
            swi_tray.CheckedChanged += (_, _) => SaveSettings();
            swi_auto_refresh.CheckedChanged += (_, _) => SaveSettings();
            input_auto_refresh.ValueChanged += (_, _) => SaveSettings();
            swi_notify_end.CheckedChanged += (_, _) => SaveSettings();
            swi_startup.CheckedChanged += (_, _) => SaveSettings();
            inputCheckInterval.ValueChanged += (_, _) => SaveSettings();
            setting_select_jyx.SelectedValueChanged += (_, _) => { SaveSettings(); _ = UpdateBanPreviewAsync(); };
            setting_select_xyx.SelectedValueChanged += (_, _) => { SaveSettings(); _ = UpdatePickPreviewAsync(); };
            _aramBenchPriority.SelectedValueChanged += (_, _) => SaveSettings();
            _autoSwapAramBench.CheckedChanged += (_, _) => SaveSettings();
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
            _config.OpggManualBuildSelections = latest.OpggManualBuildSelections;
            _settingsStore.Save(_config);
        }

        /// <summary>
        /// 将需要立即生效的设置同步到系统或其它模块。
        /// </summary>
        private static void ApplySideEffects(AssistantSettings config)
        {
            ApplyStartupSetting(config.LaunchOnStartup);
            GameMain? main = Program.GameMain;
            if (main is null || main.IsDisposed) return;

            main.ApplyWindowSettings(config);
            main.ApplyTheme();
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
        {
            var panel = CreateSegmentPanel();
            var layout = CreateSegmentLayout();
            panel.Controls.Add(layout);
            int row = 0;
            AddSectionHeader(layout, row++, "匹配与对局");
            AddSegmentRow(layout, row++, "自动匹配：", swi_open);
            AddSegmentRow(layout, row++, "自动接受：", swi_gametrue);
            AddSegmentRow(layout, row++, "接受延迟（毫秒）：", CreateAcceptDelayRow(), "在最小与最大值之间随机延迟；均为 0 时立即接受。");
            AddSegmentRow(layout, row++, "对局自动刷新：", swi_auto_refresh);
            flow_auto_refresh.Height = 40;
            label_refresh_interval.Height = 32;
            input_auto_refresh.Height = 32;
            AddSegmentRow(layout, row++, "刷新间隔：", flow_auto_refresh);
            AddSegmentRow(layout, row++, "对局结束提醒：", swi_notify_end);
            AddSectionHeader(layout, row++, "英雄选择");
            AddSegmentRow(layout, row++, "自动禁英雄：", swi_jyyx);
            AddSegmentRow(layout, row++, "禁用英雄列表：", setting_select_jyx);
            flow_ban_preview.Height = 96;
            AddSegmentRow(layout, row++, "禁用预览：", flow_ban_preview);
            layout.SetColumnSpan(flow_ban_preview, 1);
            AddSegmentRow(layout, row++, "自动选英雄：", swi_xyx);
            AddSegmentRow(layout, row++, "仅预选，不锁定：", _preselectOnly, "开启后只预选英雄，不会自动锁定。");
            AddSegmentRow(layout, row++, "补位跳过选人：", _skipFill, "客户端明确标记补位时，跳过自动选人。");
            AddSegmentRow(layout, row++, "选用英雄列表：", setting_select_xyx);
            flow_pick_preview.Height = 96;
            AddSegmentRow(layout, row++, "选用预览：", flow_pick_preview);
            layout.SetColumnSpan(flow_pick_preview, 1);
            AddSegmentRow(layout, row++, "大乱斗自动交换：", _autoSwapAramBench,
                "仅在大乱斗选人阶段，备战席有更高优先级英雄时交换。");
            AddSegmentRow(layout, row++, "备战席优先级：", _aramBenchPriority,
                "按选取顺序排列优先级；先选的英雄优先。取消选择后可重新排序。");
            AddSectionHeader(layout, row++, "赛后操作");
            AddSegmentRow(layout, row++, "自动点赞：", _autoHonor);
            AddSegmentRow(layout, row++, "自动返回大厅：", _autoReturn);
            AddSegmentRow(layout, row++, "返回后继续匹配：", _returnAndSearch, "需同时开启自动返回大厅，返回后尝试开始匹配。");
            AddSectionHeader(layout, row++, "大厅与选人操作");
            AddSegmentRow(layout, row++, "队列 ID：", _quickQueue, "快速创建大厅的队列 ID，默认 430 为匹配模式。");
            AddSegmentRow(layout, row++, "操作：", CreateMatchActions());
            AddSegmentRow(layout, row++, "操作结果：", _matchToolsStatus);
            AddSegmentRow(layout, row++, "说明：", CreateNote("本页的开关、延迟、队列和英雄列表会自动保存。"));
            return panel;
        }

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
            AddSaveRow(layout, 8, "保存客户端设置");
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
            _battleOverlayHotkey.KeyDown += (_, e) =>
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                if (!WindowHoldController.TryParseGameHotkey(WindowHoldController.FormatGameHotkey(e.KeyData), out Keys key)) return;
                _battleOverlayHotkey.Tag = key;
                _battleOverlayHotkey.Text = key == Keys.Oem3 ? "·" : WindowHoldController.FormatGameHotkey(key);
            };
            _battleOverlayHotkey.Click += (_, _) => _battleOverlayHotkey.Focus();
            _battleOverlayHotkey.Enter += (_, _) => Program.GameMain.SetWindowHotkeyCapturePaused(true);
            _battleOverlayHotkey.Leave += (_, _) => Program.GameMain.SetWindowHotkeyCapturePaused(false);
            _themeMode.Items.AddRange(new object[] { "跟随系统", "浅色", "深色" });
            _languageMode.Items.AddRange(new object[] { "中文", "English" });
            var holdNote = CreateNote("点击输入框后按一个按键。按住该键时助手会以不抢焦点的方式临时置顶，松开后隐藏到托盘；默认是键盘左上角的 · 键。\nWindows 会接管已注册的按键；快捷键不会注入或修改游戏客户端。");

            AddSegmentRow(layout, 0, "界面主题：", _themeMode,
                "界面配色：跟随系统、浅色或深色，保存后立即生效。");
            AddSegmentRow(layout, 1, "界面语言：", _languageMode, "保存后立即切换中英文界面。");
            AddSegmentRow(layout, 2, "窗口透明度：", opacityPanel,
                "助手窗口的不透明度（40–100），数值越小越透明。");
            AddSegmentRow(layout, 3, "按住置顶键：", _hotkey,
                "点击输入框后按一个键即可设定。按住该键时助手临时置顶且不抢焦点，松开后隐藏到托盘。");
            AddSegmentRow(layout, 4, "快捷键范围：", _onlyLeagueFocused,
                "开启时只有 LOL 位于前台才响应置顶键；关闭则任何窗口下都响应。");
            AddSegmentRow(layout, 5, "置顶说明：", holdNote);
            AddSectionHeader(layout, 6, "局内对局信息面板");
            AddSegmentRow(layout, 7, "对局信息：", _battleOverlayEnabled);
            AddSegmentRow(layout, 8, "面板快捷键：", _battleOverlayHotkey,
                "默认 · 键，可设置单键或 Ctrl / Alt / Shift 组合键。按住显示，松开或切出游戏后隐藏。");
            AddSegmentRow(layout, 9, "面板说明：", CreateNote("游戏位于前台时，面板快捷键显示独立的蓝红双方信息表，不抢焦点。\n启用后，游戏中使用面板快捷键；客户端或桌面仍使用上面的按住置顶键。\n可查看段位、赛季战绩、近期胜负、英雄样本胜率和本局 KDA / 补刀 / 视野得分。窗口或无边框模式下使用。"));
            AddSectionHeader(layout, 10, "窗口行为");
            AddSegmentRow(layout, 11, "最小化到托盘：", swi_tray);
            AddSegmentRow(layout, 12, "游戏分辨率：", select_resolution);
            AddSaveRow(layout, 13, "保存窗口设置");
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
                SaveShoutSettings);
            _quickShoutForm.LoadSettings(_settingsStore.Load());
            return _quickShoutForm;
        }

        /// <summary>从配置的喊话文案中随机选择并发送到游戏。</summary>
        public Task SendRandomQuickShoutToGameAsync(bool custom) =>
            _quickShoutForm?.SendRandomToGameAsync(custom) ?? Task.CompletedTask;

        /// <summary>将当前选中的喊话文案发送到游戏。</summary>
        public Task SendSelectedQuickShoutToGameAsync() =>
            _quickShoutForm?.SendSelectedBatchToGameAsync() ?? Task.CompletedTask;

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
            var panel = CreateSegmentPanel();
            var layout = CreateSegmentLayout();
            panel.Controls.Add(layout);

            _gameKdaAnnouncementEnabled.CheckedChanged += (_, _) =>
            {
                _gameKdaOnePlayerPerLine.Enabled = true;
                SaveKdaAnnouncementSettings();
            };
            _gameKdaOnePlayerPerLine.CheckedChanged += (_, _) => SaveKdaAnnouncementSettings();
            _champSelectKdaAnnouncementEnabled.CheckedChanged += (_, _) => SaveKdaAnnouncementSettings();

            _provider.Items.AddRange(Enum.GetValues<LOL_GameAssistant.Domain.Settings.AiProvider>().Cast<object>().ToArray());
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
            refreshPanel.Controls.Add(new AntdUI.Label { Text = "间隔（秒）", AutoSize = true, Padding = new Padding(10, 5, 0, 0) });
            refreshPanel.Controls.Add(_dynamicSeconds);

            _showPopup.Text = "生成 AI 建议后显示提醒";
            _overlayPosition.Items.AddRange(new object[] { "左下", "左上", "右下", "右上", "屏幕中央" });
            var overlayOffsetPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            overlayOffsetPanel.Controls.Add(new AntdUI.Label { Text = "横向", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
            overlayOffsetPanel.Controls.Add(_overlayOffsetX);
            overlayOffsetPanel.Controls.Add(new AntdUI.Label { Text = "纵向", AutoSize = true, Padding = new Padding(8, 5, 0, 0) });
            overlayOffsetPanel.Controls.Add(_overlayOffsetY);
            overlayOffsetPanel.Controls.Add(new AntdUI.Label { Text = "停留秒数", AutoSize = true, Padding = new Padding(8, 5, 0, 0) });
            overlayOffsetPanel.Controls.Add(_overlayDuration);
            var overlayNote = CreateNote("浮窗背景完全透明，只显示带深色描边的文字，不遮挡游戏画面；默认显示在游戏左下角，不抢键盘焦点；横向/纵向偏移以所选角落为基准。关闭此项后，建议只会更新到“智能建议”页。 ");
            var privacyNote = CreateNote("隐私说明：启用 AI 时间线建议并主动保存后，当前英雄、游戏阶段、可见阵容、游戏时间、金币与已购装备会发送给所选 AI 服务商以生成建议；不会发送 LCU Token、账号密码、玩家身份或本机聊天内容。未配置 API Key 或模型时不会发送数据，也不会显示替代建议。");
            var opggNote = CreateNote("开启后，助手会在选人阶段弹出图文方案。只有选中并点击应用才会写入客户端。若符文页已满，只会替换本助手创建的旧页；没有可替换页时会提示先在客户端释放额度。替换失败会尝试恢复原页。");
            var kdaAnnouncementNote = CreateNote("选人发送只汇总我方，通过客户端群聊发送一次；对局发送默认蓝方、红方各一条。勾选“单人一行”后，每名玩家各发送一条，标准 5v5 共 10 条，消息会更紧凑。对局发送沿用喊话页的游戏内发送方式与“所有人”选项。两者统计最近 30 天内同模式全部有效对局，满 5 场按累计 KDA 分档；不足 5 场标“样本不足”。若玩家资料暂不可查，会在消息中标注。选人模板支持 {players}、{allies}。");

            AddSegmentRow(layout, 0, "AI 时间线：", _recommendationEnabled,
                "开启后按设定间隔采集对局上下文并请求 AI 生成时间线建议；关闭则不请求，也不发送任何数据。");
            AddSegmentRow(layout, 1, "OP.GG 推荐：", _opggBuildAssistantEnabled,
                "选人阶段检测到你已选定英雄时弹出 OP.GG 图文方案；选中并点击应用后才会写入本机符文页与自定义物品集。");
            AddSegmentRow(layout, 2, "OP.GG 说明：", opggNote);
            AddSegmentRow(layout, 3, "选人 KDA 发送：", _champSelectKdaAnnouncementEnabled,
                "选人阶段我方战绩加载完成后，把近期 KDA 评估通过客户端聊天发送一次；只汇总我方玩家。");
            AddSegmentRow(layout, 4, "发送文案：", _champSelectKdaAnnouncementTemplate,
                "发送用的模板，支持 {players} 与 {allies}（两者内容相同，都是我方名单）。");
            AddSegmentRow(layout, 5, "对局 KDA 发送：", _gameKdaAnnouncementEnabled,
                "游戏进行中双方玩家的近期 KDA 评估加载完成后，按喊话页设置发送到游戏聊天，一局一次；会包含敌方玩家。也可在“一键喊话”设置中配置手动 KDA 快捷键（默认 F9），关闭自动发送后仍能手动发送。");
            AddSegmentRow(layout, 6, "对局发送排版：", _gameKdaOnePlayerPerLine,
                "勾选后每名玩家各发一条，游戏聊天中每人占一行；不勾选时蓝方、红方各发一条。同时用于自动发送和快捷键手动发送。");
            AddSegmentRow(layout, 7, "KDA 说明：", kdaAnnouncementNote);
            AddSegmentRow(layout, 8, "服务商：", providerPanel,
                "这一行有三个按钮：“获取 API Key”打开服务商密钥页；“获取可用模型”用当前密钥读取服务端支持的模型名；“测试连接”用当前填写的服务商、模型与密钥发一次最小请求。");
            AddSegmentRow(layout, 9, "模型名称：", _model,
                "要调用的模型名，建议先点“获取可用模型”再从这里选。模型名由服务商决定，写错时只会得到 400。");
            AddSegmentRow(layout, 10, "接口地址：", _baseUrl,
                "API 基础地址；远端必须使用 HTTPS。仅 localhost 或 127.0.0.1 可使用未加密 HTTP，请只连接可信本机服务。");
            AddSegmentRow(layout, 11, "API Key：", keyPanel,
                "填写后保存即可。已保存的密钥不会回显，留空保存表示保留原密钥；“清除已保存密钥”会在下次保存时删除它。");
            AddSegmentRow(layout, 12, "密钥状态：", _apiKeyStatus,
                "当前密钥的保存状态；密钥使用 Windows 用户级加密存放。");
            AddSegmentRow(layout, 13, "连通性：", _aiTestStatus,
                "“测试连接”的结果，会显示服务端返回的真实原因，例如模型名称不存在、Key 无效或余额不足。");
            AddSegmentRow(layout, 14, "数据与隐私：", privacyNote);
            AddSegmentRow(layout, 15, "动态建议：", refreshPanel,
                "对局中按间隔自动重新生成建议；间隔越长越省额度。");
            AddSegmentRow(layout, 16, "建议提醒：", _showPopup,
                "生成新建议后弹出提醒。与“游戏内浮窗”共用同一个显示，两者任一开启就会弹出浮窗。");
            AddSegmentRow(layout, 17, "游戏内浮窗：", _overlayEnabled,
                "在游戏内显示建议浮窗，背景透明只显示文字，不抢键盘焦点；关闭后建议只会更新到“智能建议”页。");
            AddSegmentRow(layout, 18, "浮窗位置：", _overlayPosition,
                "浮窗停靠的屏幕角落。");
            AddSegmentRow(layout, 19, "位置与时长：", overlayOffsetPanel,
                "横向/纵向偏移以所选角落为基准；停留秒数是浮窗自动隐藏前的显示时长。");
            AddSegmentRow(layout, 20, "浮窗说明：", overlayNote);
            AddSegmentRow(layout, 21, "符文自动配置：", _autoApplyRuneBuild,
                "按英雄、模式和分路优先选择已启用的个人方案；没有匹配方案时自动应用 OP.GG 推荐。仅选人阶段执行一次，手动选择方案不受影响。");
            AddSegmentRow(layout, 22, "海克斯侧边栏：", _mayhemOverlayEnabled,
                "仅海克斯大乱斗时显示专用浮窗；可扫描本机屏幕识别正在展示的增幅卡片并比较胜率。");
            AddSegmentRow(layout, 23, "选人伴随窗：", _champSelectCompanionEnabled,
                "选人时跟随客户端的位置、大小和最小化状态，仅展示可见队友的最近表现与自动选禁状态。");
            AddSaveRow(layout, 24, "保存 AI 设置");
            return panel;
        }

        // AntdUI.Panel 自身不提供滚动条；此处仅作为滚动内容承载容器，内部控件仍使用 AntdUI。
        private static AntdUI.Panel CreateSegmentPanel() => new()
        {
            AutoScroll = true,
            Padding = new Padding(UiMetrics.SpaceMedium)
        };

        /// <summary>保存逐人 KDA 测评及发送相关配置。</summary>
        private void SaveKdaAnnouncementSettings()
        {
            if (_isLoading) return;
            try
            {
                AssistantSettings latest = _settingsStore.Load();
                latest.GameKdaAnnouncementEnabled = _gameKdaAnnouncementEnabled.Checked;
                latest.GameKdaOnePlayerPerLine = _gameKdaOnePlayerPerLine.Checked;
                latest.ChampSelectKdaAnnouncementEnabled = _champSelectKdaAnnouncementEnabled.Checked;
                _settingsStore.Save(latest);
                _config.GameKdaAnnouncementEnabled = latest.GameKdaAnnouncementEnabled;
                _config.GameKdaOnePlayerPerLine = latest.GameKdaOnePlayerPerLine;
                _config.ChampSelectKdaAnnouncementEnabled = latest.ChampSelectKdaAnnouncementEnabled;
                Program.GameMain?.liveGameForm?.RefreshKdaAnnouncements();
            }
            catch (Exception ex)
            {
                UiMessage.error(Program.GameMain, $"设置保存失败：{ex.Message}");
            }
        }

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
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
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
            _languageMode.SelectedIndex = _config.LanguageMode == "en-US" ? 1 : 0;
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
            _gameKdaOnePlayerPerLine.Enabled = true;
            _champSelectKdaAnnouncementTemplate.Text = _config.ChampSelectKdaAnnouncementTemplate;
            _provider.SelectedValue = ai.Provider;
            if (_provider.SelectedIndex < 0) _provider.SelectedValue = LOL_GameAssistant.Domain.Settings.AiProvider.OpenAI;
            _model.Text = ai.Model;
            _baseUrl.Text = ai.GetBaseUrl();
            _dynamicRefresh.Checked = ai.DynamicRefreshEnabled;
            _dynamicSeconds.Value = ai.DynamicRefreshSeconds;
            _showPopup.Checked = ai.ShowRecommendationPopup;
            _overlayEnabled.Checked = ai.RecommendationOverlayEnabled;
            _overlayPosition.SelectedIndex = OverlayPositionToIndex(ai.RecommendationOverlayPosition);
            _overlayOffsetX.Value = ai.RecommendationOverlayOffsetX;
            _overlayOffsetY.Value = ai.RecommendationOverlayOffsetY;
            _overlayDuration.Value = ai.RecommendationOverlayDurationSeconds;
            _apiKeyStatus.Text = string.IsNullOrWhiteSpace(ai.EncryptedApiKey) ? "未保存" : "已加密保存在当前 Windows 用户下";
        }

        /// <summary>从控件收集扩展选项并保存。</summary>
        private void SaveExtendedSettings()
        {
            if (_isLoading) return;

            AssistantSettings overlayCandidate = _settingsStore.Load();
            overlayCandidate.BattleOverlayEnabled = _battleOverlayEnabled.Checked;
            overlayCandidate.BattleOverlayHotkey = WindowHoldController.FormatGameHotkey(
                _battleOverlayHotkey.Tag is Keys overlayKey ? overlayKey : Keys.Oem3);
            if (WindowHoldController.HasBattleOverlayHotkeyConflict(overlayCandidate))
            {
                UiMessage.warn(Program.GameMain, "对局面板快捷键与喊话或 KDA 快捷键冲突，请更换按键后保存。");
                return;
            }

            _config.GameClientPath = _gameClientLauncher.NormalizeConfiguredDirectory(_clientPath.Text);
            _config.AutoLaunchGameClient = _autoLaunchClient.Checked;
            _config.WindowOpacityPercent = (int)_opacity.Value;
            _config.ThemeMode = _themeMode.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "System" };
            _config.LanguageMode = _languageMode.SelectedIndex == 1 ? "en-US" : "zh-CN";
            _config.HoldToTopHotkey = (_hotkey.Tag is Keys holdKey ? holdKey : WindowHoldController.ParseKey(_config.HoldToTopHotkey)).ToString();
            _config.HoldToTopOnlyWhenLeagueFocused = _onlyLeagueFocused.Checked;
            _config.BattleOverlayEnabled = overlayCandidate.BattleOverlayEnabled;
            _config.BattleOverlayHotkey = overlayCandidate.BattleOverlayHotkey;

            CloudAiSettings ai = _config.Ai;
            ai.RecommendationEnabled = _recommendationEnabled.Checked;
            _config.OpggBuildAssistantEnabled = _opggBuildAssistantEnabled.Checked;
            _config.AutoApplyRuneBuild = _autoApplyRuneBuild.Checked;
            _config.MayhemOverlayEnabled = _mayhemOverlayEnabled.Checked;
            _config.ChampSelectCompanionEnabled = _champSelectCompanionEnabled.Checked;
            _config.ChampSelectKdaAnnouncementEnabled = _champSelectKdaAnnouncementEnabled.Checked;
            _config.GameKdaAnnouncementEnabled = _gameKdaAnnouncementEnabled.Checked;
            _config.GameKdaOnePlayerPerLine = _gameKdaOnePlayerPerLine.Checked;
            _config.ChampSelectKdaAnnouncementTemplate = _champSelectKdaAnnouncementTemplate.Text;
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
            ai.DynamicRefreshEnabled = _dynamicRefresh.Checked;
            ai.DynamicRefreshSeconds = (int)_dynamicSeconds.Value;
            ai.ShowRecommendationPopup = _showPopup.Checked;
            ai.RecommendationOverlayEnabled = _overlayEnabled.Checked;
            ai.RecommendationOverlayPosition = IndexToOverlayPosition(_overlayPosition.SelectedIndex);
            ai.RecommendationOverlayOffsetX = (int)_overlayOffsetX.Value;
            ai.RecommendationOverlayOffsetY = (int)_overlayOffsetY.Value;
            ai.RecommendationOverlayDurationSeconds = (int)_overlayDuration.Value;
            if (_clearAiKey) ai.EncryptedApiKey = "";
            else if (!string.IsNullOrWhiteSpace(_apiKey.Text)) ai.EncryptedApiKey = _settingsSecretProtector.Protect(_apiKey.Text);
            _config.Normalize();

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
            label_cache_status.Text = $"已缓存: {_settingsStore.GetStoragePath()}";
            _apiKey.Clear();
            _clearAiKey = false;
            _apiKeyStatus.Text = string.IsNullOrWhiteSpace(ai.EncryptedApiKey) ? "未保存" : "已加密保存在当前 Windows 用户下";
            LOL_GameAssistant.Helper.UiMessage.success(Program.GameMain,
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
                _clientPath.Text = dialog.SelectedPath;
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
                result = await _gameClientLauncher.StartAndVerifyAsync(_clientPath.Text.Trim());
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
                _launchClientButton.Enabled = true;
            }
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
                string reply = await AppCompositionRoot.AiRecommendationProvider.TestAsync(BuildAiSettingsFromUi());
                SetAiTestStatus($"连接正常，模型回复：{reply}", Color.ForestGreen);
            }
            catch (Exception ex)
            {
                SetAiTestStatus(ex.Message, Color.Firebrick);
            }
            finally
            {
                _testAi.Enabled = true;
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
                IReadOnlyList<string> models = await AppCompositionRoot.AiRecommendationProvider.ListModelsAsync(BuildAiSettingsFromUi());
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
                _fetchModels.Enabled = true;
            }
        }

        /// <summary>更新 AI 服务测试的状态提示。</summary>
        private void SetAiTestStatus(string text, Color color)
        {
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

        /// <summary>按当前客户端启动配置发起启动操作。</summary>
        private void LaunchLeagueClient(bool showMessage)
        {
            GameClientLaunchResult result = _gameClientLauncher.Start(_config.GameClientPath);
            SetClientStatus(result);
            string resolvedDirectory = _gameClientLauncher.NormalizeConfiguredDirectory(result.ExecutablePath);
            if (!string.IsNullOrWhiteSpace(resolvedDirectory) &&
                !string.Equals(_config.GameClientPath, resolvedDirectory, StringComparison.OrdinalIgnoreCase))
            {
                _config.GameClientPath = resolvedDirectory;
                _clientPath.Text = _config.GameClientPath;
                SaveConfigPreservingSelections();
            }

            Program.GameMain.infoMsg.AddMsg(result.Message);
            if (showMessage)
            {
                if (result.Started) LOL_GameAssistant.Helper.UiMessage.success(Program.GameMain, result.Message);
                else LOL_GameAssistant.Helper.UiMessage.error(Program.GameMain, result.Message);
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
        private static void ApplyStartupSetting(bool enabled)
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

        private static readonly Dictionary<int, Image> _championIconCache = new();
        private static readonly object _iconCacheLock = new();

        /// <summary>
        /// 获取英雄头像（带缓存）
        /// </summary>
        private static async Task<Image?> GetChampionIconAsync(int championId)
        {
            lock (_iconCacheLock)
            {
                if (_championIconCache.TryGetValue(championId, out var cached))
                    return cached;
            }
            try
            {
                var asset = await AppCompositionRoot.GameAssetService.GetChampionIconAsync(championId);
                if (asset == null || asset.IsEmpty) return null;
                using var stream = new MemoryStream(asset.Content);
                using var source = Image.FromStream(stream);
                var img = new Bitmap(source);
                lock (_iconCacheLock)
                {
                    if (!_championIconCache.ContainsKey(championId))
                        _championIconCache[championId] = img;
                }
                return img;
            }
            catch { return null; }
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
            ControlLifetime.ClearAndDispose(panel);
            if (championIds.Count == 0) return;

            var tasks = championIds.Select(async id =>
            {
                var img = await GetChampionIconAsync(id);
                return (id, img);
            }).ToList();

            var results = await Task.WhenAll(tasks);
            if (IsDisposed || panel.IsDisposed)
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

        #endregion 本地缓存

        #region 定时执行方法

        /// <summary>启用自动匹配时发起匹配请求，并用十秒间隔抑制重复调用。</summary>
        public static void OpenGame(SettingForm form)
        {
            var now = DateTime.Now;
            var lastOpen = form.lastOpenGameTime;
            // 直接读取持久化配置，避免“设置页从未打开时 _config 未加载”导致自动匹配不生效
            if ((lastOpen == null || (now - lastOpen.Value).TotalSeconds >= 10) &&
                AppCompositionRoot.ApplicationSettingsStore.Load().AutoMatch)
            {
                _ = AppCompositionRoot.LobbyService.StartMatchmakingAsync();
                form.lastOpenGameTime = now;
            }
        }

        /// <summary>启用自动接受时向客户端提交接受匹配请求。</summary>
        public static void GameTrue(SettingForm form)
        {
            if (AppCompositionRoot.ApplicationSettingsStore.Load().AutoAccept)
            {
                _ = AppCompositionRoot.LobbyService.AcceptReadyCheckAsync();
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
            var allChampions = AppCompositionRoot.ChampionCatalog.GetAll();
            foreach (var champion in allChampions)
            {
                this.setting_select_jyx.Items.Add(champion.DisplayName);
                this.setting_select_xyx.Items.Add(champion.DisplayName);
                _aramBenchPriority.Items.Add(champion.DisplayName);
            }
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
