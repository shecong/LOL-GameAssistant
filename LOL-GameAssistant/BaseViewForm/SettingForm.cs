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
    public partial class SettingForm : UserControl
    {
        public DateTime? lastOpenGameTime = null;
        private AssistantSettings _config;
        private bool _isLoading;
        private ToolTip toolTip1 = new ToolTip();
        private readonly IGameClientLauncher _gameClientLauncher;
        private readonly IApplicationSettingsStore _settingsStore;
        private readonly ISettingsSecretProtector _settingsSecretProtector;
        private readonly AntdUI.Segmented _settingsSegmented = new();
        private readonly Panel _settingsSegmentHost = new();
        private readonly Control[] _settingsSegments = new Control[4];
        private readonly TextBox _clientPath = new() { Dock = DockStyle.Fill };
        private readonly CheckBox _autoLaunchClient = new() { Text = "启动助手时直接启动 LOL 客户端", AutoSize = true };
        private readonly Label _clientStatus = new() { AutoSize = true, ForeColor = Color.DimGray };
        private readonly NumericUpDown _opacity = new() { Minimum = 40, Maximum = 100, Width = 130 };
        private readonly ComboBox _themeMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        private readonly TextBox _hotkey = new() { ReadOnly = true, Width = 160, TabStop = true };
        private readonly CheckBox _onlyLeagueFocused = new() { Text = "仅在 LOL 位于前台时响应", AutoSize = true };
        private readonly CheckBox _quickMessageEnabled = new() { Text = "启用快捷消息复制", AutoSize = true };
        private readonly ComboBox _quickMessageLanguage = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        private readonly TextBox _quickMessageText = new() { Dock = DockStyle.Fill, Multiline = true, Height = 90 };
        private readonly TextBox _quickMessageHotkey = new() { ReadOnly = true, Width = 160, TabStop = true };
        private readonly NumericUpDown _quickMessageInterval = new() { Minimum = 2, Maximum = 30, Width = 100 };
        private readonly CheckBox _recommendationEnabled = new() { Text = "启用 AI 时间线建议", AutoSize = true };
        private readonly CheckBox _opggBuildAssistantEnabled = new() { Text = "启用 OP.GG 选人出装与符文推荐", AutoSize = true };
        private readonly CheckBox _champSelectKdaAnnouncementEnabled = new() { Text = "选人加载完成后发送 KDA 评估到聊天", AutoSize = true };
        private readonly TextBox _champSelectKdaAnnouncementTemplate = new() { Dock = DockStyle.Fill, Multiline = true, Height = 86, ScrollBars = ScrollBars.Vertical };
        private readonly ComboBox _provider = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
        private readonly TextBox _model = new() { Width = 290 };
        private readonly TextBox _baseUrl = new() { Dock = DockStyle.Fill };
        private readonly TextBox _apiKey = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true, PlaceholderText = "留空则保留已保存的密钥" };
        private readonly Label _apiKeyStatus = new() { AutoSize = true, ForeColor = Color.DimGray };
        private readonly CheckBox _dynamicRefresh = new() { Text = "游戏中自动刷新建议", AutoSize = true };
        private readonly NumericUpDown _dynamicSeconds = new() { Minimum = 15, Maximum = 600, Width = 100 };
        private readonly CheckBox _showPopup = new() { Text = "建议刷新后弹出提醒", AutoSize = true };
        private readonly CheckBox _overlayEnabled = new() { Text = "游戏内显示建议浮窗", AutoSize = true };
        private readonly ComboBox _overlayPosition = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        private readonly NumericUpDown _overlayOffsetX = new() { Minimum = -600, Maximum = 600, Width = 80 };
        private readonly NumericUpDown _overlayOffsetY = new() { Minimum = -600, Maximum = 600, Width = 80 };
        private readonly NumericUpDown _overlayDuration = new() { Minimum = 3, Maximum = 30, Width = 80 };
        private bool _clearAiKey;
        private static readonly string[] ResolutionPresets = { "1280x720", "1366x768", "1600x900", "1920x1080", "2560x1440", "3840x2160" };

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
            ISettingsSecretProtector settingsSecretProtector)
        {
            _gameClientLauncher = gameClientLauncher;
            _settingsStore = settingsStore;
            _settingsSecretProtector = settingsSecretProtector;
            InitializeComponent();
            _config = new AssistantSettings();
            InitializeSegmentedSettings();
        }

        private async void SettingForm_Load(object sender, EventArgs e)
        {
            await LoadCachedSettings();
            await LoadBase();
            ApplySideEffects(_config);

            // 自动启动由 GameMain 的启动生命周期统一执行，避免依赖用户是否打开“设置”标签。
        }

        private void InitializeSegmentedSettings()
        {
            Controls.Remove(gridPanel2);

            _settingsSegmented.Dock = DockStyle.Top;
            _settingsSegmented.Height = 42;
            _settingsSegmented.Full = true;
            _settingsSegmented.Margin = new Padding(10, 8, 10, 6);
            _settingsSegmented.Items.Add(new AntdUI.SegmentedItem { Text = "通用" });
            _settingsSegmented.Items.Add(new AntdUI.SegmentedItem { Text = "客户端" });
            _settingsSegmented.Items.Add(new AntdUI.SegmentedItem { Text = "窗口与快捷键" });
            _settingsSegmented.Items.Add(new AntdUI.SegmentedItem { Text = "AI 设置" });
            _settingsSegmented.SelectIndexChanged += (_, e) => ShowSettingsSegment(e.Value);

            _settingsSegmentHost.Dock = DockStyle.Fill;
            gridPanel2.Dock = DockStyle.Fill;
            _settingsSegments[0] = gridPanel2;
            _settingsSegments[1] = CreateClientSegment();
            _settingsSegments[2] = CreateWindowSegment();
            _settingsSegments[3] = CreateAiSegment();
            foreach (Control segment in _settingsSegments)
            {
                segment.Dock = DockStyle.Fill;
                segment.Visible = false;
                _settingsSegmentHost.Controls.Add(segment);
            }

            var root = new Panel { Dock = DockStyle.Fill };
            root.Controls.Add(_settingsSegmentHost);
            root.Controls.Add(_settingsSegmented);
            Controls.Add(root);
            _settingsSegmented.SelectIndex = 0;
            ShowSettingsSegment(0);
        }

        private void ShowSettingsSegment(int index)
        {
            if (index < 0 || index >= _settingsSegments.Length) return;
            for (int i = 0; i < _settingsSegments.Length; i++)
                _settingsSegments[i].Visible = i == index;
        }

        #region 本地缓存

        private async Task LoadCachedSettings()
        {
            _isLoading = true;
            _config = _settingsStore.Load();

            label_cache_status.Text = $"缓存文件: {_settingsStore.GetStoragePath()}";

            swi_open.Checked = _config.AutoMatch;
            swi_gametrue.Checked = _config.AutoAccept;
            swi_jyyx.Checked = _config.AutoBan;
            swi_xyx.Checked = _config.AutoPick;
            swi_tray.Checked = _config.MinimizeToTray;
            swi_auto_refresh.Checked = _config.AutoRefresh;
            input_auto_refresh.Value = _config.AutoRefreshIntervalSeconds;
            swi_notify_end.Checked = _config.NotifyOnGameEnd;
            swi_startup.Checked = _config.LaunchOnStartup;

            inputNumber1.Value = _config.CheckIntervalSeconds;
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
            inputNumber1.ValueChanged += (_, _) => SaveSettings();
            setting_select_jyx.SelectedValueChanged += (_, _) => { SaveSettings(); _ = UpdateBanPreviewAsync(); };
            setting_select_xyx.SelectedValueChanged += (_, _) => { SaveSettings(); _ = UpdatePickPreviewAsync(); };

            _isLoading = false;
        }

        private void SaveSettings()
        {
            if (_isLoading) return;

            _config.AutoMatch = swi_open.Checked;
            _config.AutoAccept = swi_gametrue.Checked;
            _config.AutoBan = swi_jyyx.Checked;
            _config.AutoPick = swi_xyx.Checked;
            _config.MinimizeToTray = swi_tray.Checked;
            _config.AutoRefresh = swi_auto_refresh.Checked;
            _config.AutoRefreshIntervalSeconds = (int)input_auto_refresh.Value;
            _config.NotifyOnGameEnd = swi_notify_end.Checked;
            _config.LaunchOnStartup = swi_startup.Checked;
            _config.CheckIntervalSeconds = (int)inputNumber1.Value;
            _config.BanChampions = GetSelectedTexts(setting_select_jyx);
            _config.PickChampions = GetSelectedTexts(setting_select_xyx);

            if (select_resolution.SelectedIndex >= 0 && select_resolution.SelectedIndex < ResolutionPresets.Length)
                _config.Resolution = ResolutionPresets[select_resolution.SelectedIndex];

            _settingsStore.Save(_config);
            ApplySideEffects(_config);
            label_cache_status.Text = $"已缓存: {_settingsStore.GetStoragePath()}";
        }

        /// <summary>
        /// 将需要立即生效的设置同步到系统或其它模块。
        /// </summary>
        private static void ApplySideEffects(AssistantSettings config)
        {
            ApplyStartupSetting(config.LaunchOnStartup);
            Program.GameMain.ApplyWindowSettings(config);
            Program.GameMain.ApplyTheme();
            GameMain.liveGameForm.ConfigureAutoRefresh(
                config.AutoRefresh,
                Math.Max(10, config.AutoRefreshIntervalSeconds));
            Program.GameMain.ApplyRecommendationSettings(config);
            GameMain.coachForm.RefreshOpggAvailability();
            GameMain.liveGameForm.RefreshChampSelectKdaAnnouncement();
        }

        private Panel CreateClientSegment()
        {
            var panel = CreateSegmentPanel();
            var layout = CreateSegmentLayout();
            panel.Controls.Add(layout);

            var browse = new Button { Text = "选择安装文件夹", AutoSize = true, Dock = DockStyle.Right };
            browse.Click += (_, _) => BrowseClientExecutable();
            var pathPanel = new Panel { Dock = DockStyle.Fill, Height = 32 };
            pathPanel.Controls.Add(_clientPath);
            pathPanel.Controls.Add(browse);

            var launch = new Button { Text = "立即启动 LOL", AutoSize = true };
            launch.Click += (_, _) => StartLeagueClientFromSettings();
            var note = CreateNote("选择 LOL 安装文件夹后，助手会自动扫描其子目录中的 LeagueClient.exe 并直接启动，不通过 WeGame；登录完成后会自动等待并连接 LCU。\n也可留空，由助手尝试查找常见安装位置。\n此处的自动启动仅在助手打开时执行一次，不会在每次保存设置时重复拉起客户端。");

            AddSegmentRow(layout, 0, "安装文件夹：", pathPanel);
            AddSegmentRow(layout, 1, "自动启动：", _autoLaunchClient);
            AddSegmentRow(layout, 2, "操作：", launch);
            AddSegmentRow(layout, 3, "状态：", _clientStatus);
            AddSegmentRow(layout, 4, "说明：", note);
            AddSaveRow(layout, 5, "保存客户端设置");
            return panel;
        }

        private Panel CreateWindowSegment()
        {
            var panel = CreateSegmentPanel();
            var layout = CreateSegmentLayout();
            panel.Controls.Add(layout);

            var opacityPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            opacityPanel.Controls.Add(_opacity);
            opacityPanel.Controls.Add(new Label { Text = "%（40–100）", AutoSize = true, Padding = new Padding(6, 6, 0, 0) });
            _hotkey.KeyDown += CaptureHoldToTopHotkey;
            _hotkey.Click += (_, _) => _hotkey.Focus();
            _themeMode.Items.AddRange(new object[] { "跟随系统", "浅色", "深色" });
            var holdNote = CreateNote("点击输入框后按一个按键。按住该键时助手会以不抢焦点的方式临时置顶，松开后立即最小化；默认是键盘左上角的 · 键。\n快捷键不会传给游戏，也不会注入或修改游戏客户端。");

            _quickMessageLanguage.Items.AddRange(new object[] { "中文", "English", "日本語", "한국어", "自定义" });
            _quickMessageLanguage.SelectedIndexChanged += QuickMessageLanguageChanged;
            _quickMessageHotkey.KeyDown += CaptureQuickMessageHotkey;
            _quickMessageHotkey.Click += (_, _) => _quickMessageHotkey.Focus();
            _quickMessageEnabled.Text = "启用快捷弹幕自动发送";
            var messageIntervalPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            messageIntervalPanel.Controls.Add(_quickMessageInterval);
            messageIntervalPanel.Controls.Add(new Label { Text = "秒（2–30）", AutoSize = true, Padding = new Padding(6, 6, 0, 0) });
            var messageNote = CreateNote("按下快捷键后，程序仅在《英雄联盟》对局窗口位于前台时自动执行“打开聊天框 → 通过剪贴板粘贴预设内容 → 发送”一次，确保中文与特殊字符不被输入法吞掉。为避免误触，发送之间会受最小间隔限制；不会后台循环刷屏。");

            AddSegmentRow(layout, 0, "界面主题：", _themeMode);
            AddSegmentRow(layout, 1, "窗口透明度：", opacityPanel);
            AddSegmentRow(layout, 2, "按住置顶键：", _hotkey);
            AddSegmentRow(layout, 3, "快捷键范围：", _onlyLeagueFocused);
            AddSegmentRow(layout, 4, "置顶说明：", holdNote);
            AddSegmentRow(layout, 5, "快捷消息：", _quickMessageEnabled);
            AddSegmentRow(layout, 6, "发送语言：", _quickMessageLanguage);
            AddSegmentRow(layout, 7, "预设内容：", _quickMessageText);
            AddSegmentRow(layout, 8, "发送快捷键：", _quickMessageHotkey);
            AddSegmentRow(layout, 9, "最小发送间隔：", messageIntervalPanel);
            AddSegmentRow(layout, 10, "消息说明：", messageNote);
            AddSaveRow(layout, 11, "保存窗口与快捷键设置");
            return panel;
        }

        private Panel CreateAiSegment()
        {
            var panel = CreateSegmentPanel();
            var layout = CreateSegmentLayout();
            panel.Controls.Add(layout);

            _provider.Items.AddRange(Enum.GetValues<LOL_GameAssistant.Domain.Settings.AiProvider>().Cast<object>().ToArray());
            _provider.SelectedIndexChanged += (_, _) => ApplyProviderDefaults();
            _apiKey.TextChanged += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(_apiKey.Text)) _clearAiKey = false;
            };

            var providerPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            providerPanel.Controls.Add(_provider);
            var keyPortal = new Button { Text = "获取 API Key", AutoSize = true };
            keyPortal.Click += (_, _) => OpenKeyPortal();
            providerPanel.Controls.Add(keyPortal);

            var clearApiKeyButton = new Button { Text = "清除已保存密钥", AutoSize = true, Dock = DockStyle.Right };
            clearApiKeyButton.Click += (_, _) =>
            {
                _clearAiKey = true;
                _apiKey.Clear();
                _apiKeyStatus.Text = "将在保存后清除";
            };
            var keyPanel = new Panel { Dock = DockStyle.Fill, Height = 32 };
            keyPanel.Controls.Add(_apiKey);
            keyPanel.Controls.Add(clearApiKeyButton);

            var refreshPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            refreshPanel.Controls.Add(_dynamicRefresh);
            refreshPanel.Controls.Add(new Label { Text = "间隔（秒）", AutoSize = true, Padding = new Padding(10, 5, 0, 0) });
            refreshPanel.Controls.Add(_dynamicSeconds);

            _showPopup.Text = "生成 AI 建议后显示提醒";
            _overlayPosition.Items.AddRange(new object[] { "左下", "左上", "右下", "右上", "屏幕中央" });
            var overlayOffsetPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            overlayOffsetPanel.Controls.Add(new Label { Text = "横向", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
            overlayOffsetPanel.Controls.Add(_overlayOffsetX);
            overlayOffsetPanel.Controls.Add(new Label { Text = "纵向", AutoSize = true, Padding = new Padding(8, 5, 0, 0) });
            overlayOffsetPanel.Controls.Add(_overlayOffsetY);
            overlayOffsetPanel.Controls.Add(new Label { Text = "停留秒数", AutoSize = true, Padding = new Padding(8, 5, 0, 0) });
            overlayOffsetPanel.Controls.Add(_overlayDuration);
            var overlayNote = CreateNote("浮窗默认显示在游戏左下角，不抢键盘焦点；横向/纵向偏移以所选角落为基准。关闭此项后，建议只会更新到“智能建议”页。 ");
            var privacyNote = CreateNote("隐私说明：启用 AI 时间线建议并主动保存后，当前英雄、游戏阶段、可见阵容、游戏时间、金币与已购装备会发送给所选 AI 服务商以生成建议；不会发送 LCU Token、账号密码、玩家身份或本机聊天内容。未配置 API Key 或模型时不会发送数据，也不会显示替代建议。");
            var opggNote = CreateNote("开启后，助手会在选人阶段检测到你已选定英雄时弹出图文方案。选中并点击应用后，才会从 OP.GG 读取公开推荐，并写入本机客户端的符文页与自定义物品集；取消不会修改任何内容。若符文页已满，将替换当前正在使用的符文页，不会清理其它自定义页。");
            var kdaAnnouncementNote = CreateNote("仅在英雄选择阶段、我方阵容的战绩加载完成后通过 LCU 发送一次。严格规则：取最近 100 场里同队列的最近 20 场（不计重开局），KDA ＜ 1 为“人机”、1–2.19 为下等马、2.2–4.49 为中等马、≥ 4.5 为上等马；不足 20 场显示数据不足。只汇总我方玩家，不获取也不发送敌方。模板支持 {players}、{allies}，两者内容相同（均为我方名单）。\n默认文案如下，可直接编辑：\n【选人近期 KDA 评估】\n{allies}\n玩家：上等马 87分 · KDA 4.90 · 胜率 55%");

            AddSegmentRow(layout, 0, "AI 时间线：", _recommendationEnabled);
            AddSegmentRow(layout, 1, "OP.GG 推荐：", _opggBuildAssistantEnabled);
            AddSegmentRow(layout, 2, "OP.GG 说明：", opggNote);
            AddSegmentRow(layout, 3, "选人 KDA 发送：", _champSelectKdaAnnouncementEnabled);
            AddSegmentRow(layout, 4, "发送文案：", _champSelectKdaAnnouncementTemplate);
            AddSegmentRow(layout, 5, "KDA 说明：", kdaAnnouncementNote);
            AddSegmentRow(layout, 6, "服务商：", providerPanel);
            AddSegmentRow(layout, 7, "模型名称：", _model);
            AddSegmentRow(layout, 8, "接口地址：", _baseUrl);
            AddSegmentRow(layout, 9, "API Key：", keyPanel);
            AddSegmentRow(layout, 10, "密钥状态：", _apiKeyStatus);
            AddSegmentRow(layout, 11, "数据与隐私：", privacyNote);
            AddSegmentRow(layout, 12, "动态建议：", refreshPanel);
            AddSegmentRow(layout, 13, "建议提醒：", _showPopup);
            AddSegmentRow(layout, 14, "游戏内浮窗：", _overlayEnabled);
            AddSegmentRow(layout, 15, "浮窗位置：", _overlayPosition);
            AddSegmentRow(layout, 16, "位置与时长：", overlayOffsetPanel);
            AddSegmentRow(layout, 17, "浮窗说明：", overlayNote);
            AddSaveRow(layout, 18, "保存 AI 设置");
            return panel;
        }

        private static Panel CreateSegmentPanel() => new()
        {
            AutoScroll = true,
            Padding = new Padding(10)
        };

        private static TableLayoutPanel CreateSegmentLayout()
        {
            var layout = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                Padding = new Padding(12),
                ColumnCount = 2
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return layout;
        }

        private static Label CreateNote(string text) => new()
        {
            AutoSize = true,
            MaximumSize = new Size(620, 0),
            ForeColor = Color.DimGray,
            Text = text
        };

        private static void AddSegmentRow(TableLayoutPanel layout, int row, string caption, Control control)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var label = new Label
            {
                Text = caption,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, 7, 0, 0)
            };
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            control.Margin = new Padding(3, 5, 3, 5);
            layout.Controls.Add(label, 0, row);
            layout.Controls.Add(control, 1, row);
        }

        private void AddSaveRow(TableLayoutPanel layout, int row, string text)
        {
            var save = new Button { Text = text, AutoSize = true };
            save.Click += (_, _) => SaveExtendedSettings();
            AddSegmentRow(layout, row, "", save);
        }

        private void LoadExtendedSettings()
        {
            _config.Normalize();
            _clientPath.Text = _gameClientLauncher.NormalizeConfiguredDirectory(_config.GameClientPath);
            _autoLaunchClient.Checked = _config.AutoLaunchGameClient;
            _opacity.Value = _config.WindowOpacityPercent;
            _themeMode.SelectedIndex = _config.ThemeMode switch { "Light" => 1, "Dark" => 2, _ => 0 };
            Keys holdKey = WindowHoldController.ParseKey(_config.HoldToTopHotkey);
            _hotkey.Tag = holdKey;
            _hotkey.Text = WindowHoldController.DescribeKey(holdKey);
            _onlyLeagueFocused.Checked = _config.HoldToTopOnlyWhenLeagueFocused;
            _quickMessageEnabled.Checked = _config.QuickMessageAutoSendEnabled;
            _quickMessageLanguage.SelectedItem = _config.QuickMessageLanguage;
            if (_quickMessageLanguage.SelectedIndex < 0) _quickMessageLanguage.SelectedItem = "自定义";
            _quickMessageText.Text = _config.QuickMessageText;
            _quickMessageInterval.Value = _config.QuickMessageSendIntervalSeconds;
            Keys messageKey = WindowHoldController.ParseKey(_config.QuickMessageHotkey);
            _quickMessageHotkey.Tag = messageKey;
            _quickMessageHotkey.Text = WindowHoldController.DescribeKey(messageKey);

            CloudAiSettings ai = _config.Ai;
            _recommendationEnabled.Checked = ai.RecommendationEnabled;
            _opggBuildAssistantEnabled.Checked = _config.OpggBuildAssistantEnabled;
            _champSelectKdaAnnouncementEnabled.Checked = _config.ChampSelectKdaAnnouncementEnabled;
            _champSelectKdaAnnouncementTemplate.Text = _config.ChampSelectKdaAnnouncementTemplate;
            _provider.SelectedItem = ai.Provider;
            if (_provider.SelectedIndex < 0) _provider.SelectedItem = LOL_GameAssistant.Domain.Settings.AiProvider.OpenAI;
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

        private void SaveExtendedSettings()
        {
            if (_isLoading) return;

            _config.GameClientPath = _gameClientLauncher.NormalizeConfiguredDirectory(_clientPath.Text);
            _config.AutoLaunchGameClient = _autoLaunchClient.Checked;
            _config.WindowOpacityPercent = (int)_opacity.Value;
            _config.ThemeMode = _themeMode.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "System" };
            _config.HoldToTopHotkey = (_hotkey.Tag is Keys holdKey ? holdKey : WindowHoldController.ParseKey(_config.HoldToTopHotkey)).ToString();
            _config.HoldToTopOnlyWhenLeagueFocused = _onlyLeagueFocused.Checked;
            _config.QuickMessageAutoSendEnabled = _quickMessageEnabled.Checked;
            _config.QuickMessageLanguage = _quickMessageLanguage.SelectedItem?.ToString() ?? "中文";
            _config.QuickMessageText = _quickMessageText.Text.Trim();
            _config.QuickMessageHotkey = (_quickMessageHotkey.Tag is Keys messageKey ? messageKey : WindowHoldController.ParseKey(_config.QuickMessageHotkey)).ToString();
            _config.QuickMessageSendIntervalSeconds = (int)_quickMessageInterval.Value;

            CloudAiSettings ai = _config.Ai;
            ai.RecommendationEnabled = _recommendationEnabled.Checked;
            _config.OpggBuildAssistantEnabled = _opggBuildAssistantEnabled.Checked;
            _config.ChampSelectKdaAnnouncementEnabled = _champSelectKdaAnnouncementEnabled.Checked;
            _config.ChampSelectKdaAnnouncementTemplate = _champSelectKdaAnnouncementTemplate.Text;
            ai.Provider = _provider.SelectedItem is LOL_GameAssistant.Domain.Settings.AiProvider provider
                ? provider
                : LOL_GameAssistant.Domain.Settings.AiProvider.OpenAI;
            ai.Model = _model.Text.Trim();
            ai.BaseUrl = _baseUrl.Text.Trim().TrimEnd('/');
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

            _settingsStore.Save(_config);
            ApplySideEffects(_config);
            label_cache_status.Text = $"已缓存: {_settingsStore.GetStoragePath()}";
            _apiKey.Clear();
            _clearAiKey = false;
            _apiKeyStatus.Text = string.IsNullOrWhiteSpace(ai.EncryptedApiKey) ? "未保存" : "已加密保存在当前 Windows 用户下";
            AntdUI.Message.success(Program.GameMain, "设置已保存");
        }

        private void BrowseClientExecutable()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "选择 LOL 安装文件夹（程序会自动扫描 LeagueClient.exe）",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };
            if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                _clientPath.Text = dialog.SelectedPath;
        }

        private async void StartLeagueClientFromSettings()
        {
            GameClientLaunchResult result;
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
            SetClientStatus(result);
            if (result.Started && !string.IsNullOrWhiteSpace(result.ExecutablePath))
            {
                _clientPath.Text = _gameClientLauncher.NormalizeConfiguredDirectory(result.ExecutablePath);
                _config.GameClientPath = _clientPath.Text;
                _settingsStore.Save(_config);
            }

            if (result.Started) AntdUI.Message.success(Program.GameMain, result.Message);
            else AntdUI.Message.error(Program.GameMain, result.Message);
        }

        private void CaptureHoldToTopHotkey(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu) return;
            _hotkey.Tag = e.KeyCode;
            _hotkey.Text = WindowHoldController.DescribeKey(e.KeyCode);
            e.SuppressKeyPress = true;
            e.Handled = true;
        }

        private void CaptureQuickMessageHotkey(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu) return;
            _quickMessageHotkey.Tag = e.KeyCode;
            _quickMessageHotkey.Text = WindowHoldController.DescribeKey(e.KeyCode);
            e.SuppressKeyPress = true;
            e.Handled = true;
        }

        private void QuickMessageLanguageChanged(object? sender, EventArgs e)
        {
            if (_isLoading) return;
            string language = _quickMessageLanguage.SelectedItem?.ToString() ?? "自定义";
            if (!QuickMessageTemplates.TryGet(language, out string preset)) return;
            _quickMessageText.Text = preset;
            _quickMessageText.SelectionStart = _quickMessageText.TextLength;
        }

        private static int OverlayPositionToIndex(string? value) => value switch
        {
            "TopLeft" => 1,
            "BottomRight" => 2,
            "TopRight" => 3,
            "Center" => 4,
            _ => 0
        };

        private static string IndexToOverlayPosition(int index) => index switch
        {
            1 => "TopLeft",
            2 => "BottomRight",
            3 => "TopRight",
            4 => "Center",
            _ => "BottomLeft"
        };

        private void ApplyProviderDefaults()
        {
            if (_isLoading || _provider.SelectedItem is not LOL_GameAssistant.Domain.Settings.AiProvider provider) return;
            _baseUrl.Text = new CloudAiSettings { Provider = provider }.GetBaseUrl();
        }

        private void OpenKeyPortal()
        {
            if (_provider.SelectedItem is not LOL_GameAssistant.Domain.Settings.AiProvider provider) return;
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
                _settingsStore.Save(_config);
            }

            GameMain.infoMsg.AddMsg(result.Message);
            if (showMessage)
            {
                if (result.Started) AntdUI.Message.success(Program.GameMain, result.Message);
                else AntdUI.Message.error(Program.GameMain, result.Message);
            }
        }

        private void SetClientStatus(GameClientLaunchResult result)
        {
            _clientStatus.ForeColor = result.Started ? Color.ForestGreen : Color.Firebrick;
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

        private static List<string> GetSelectedTexts(AntdUI.SelectMultiple select)
        {
            var result = new List<string>();
            try
            {
                var val = select.SelectedValue;
                if (val != null)
                {
                    // SelectMultiple 在非多选模式下返回单个值
                    string txt = val.ToString() ?? "";
                    if (txt != "" && !txt.StartsWith("System."))
                    {
                        result.Add(txt);
                        return result;
                    }
                    // 多选模式下返回数组
                    if (val.GetType().IsArray)
                    {
                        foreach (var item in (System.Collections.IEnumerable)val)
                        {
                            string? itemText = item?.ToString();
                            if (!string.IsNullOrEmpty(itemText))
                                result.Add(itemText);
                        }
                    }
                }
            }
            catch { }
            return result;
        }

        private static void SetSelectedTexts(AntdUI.SelectMultiple select, List<string> texts)
        {
            if (texts.Count == 0) return;
            try
            {
                select.SelectedValue = texts.ToArray();
            }
            catch { }
        }

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
            panel.Controls.Clear();
            if (championIds.Count == 0) return;

            var tasks = championIds.Select(async id =>
            {
                var img = await GetChampionIconAsync(id);
                return (id, img);
            }).ToList();

            var results = await Task.WhenAll(tasks);
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
                toolTip1.SetToolTip(pic, name);
                panel.Controls.Add(pic);
            }
        }

        #endregion 本地缓存

        #region 定时执行方法

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

        private async Task LoadBase()
        {
            var allChampions = AppCompositionRoot.ChampionCatalog.GetAll();
            foreach (var champion in allChampions)
            {
                this.setting_select_jyx.Items.Add(champion.DisplayName);
                this.setting_select_xyx.Items.Add(champion.DisplayName);
            }
            RestoreSelectedChampions();
        }

        /// <summary>将保存的英雄名称通过应用目录转换为稳定的英雄 ID。</summary>
        private static List<int> ResolveChampionIds(IEnumerable<string> championNames) =>
            championNames
                .Select(AppCompositionRoot.ChampionCatalog.FindIdByDisplayName)
                .OfType<int>()
                .ToList();

        private void RestoreSelectedChampions()
        {
            SetSelectedTexts(setting_select_jyx, _config.BanChampions);
            SetSelectedTexts(setting_select_xyx, _config.PickChampions);
        }
    }
}
