using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Application.Insights;
using LOL_GameAssistant.Application.Profiles;
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
        private readonly ToolTip _featureTip = new()
        {
            // 说明文本较长，默认 5 秒往往读不完。
            AutoPopDelay = 20000,
            InitialDelay = 350,
            ReshowDelay = 100,
            ShowAlways = true
        };
        private readonly IGameClientLauncher _gameClientLauncher;
        private readonly IApplicationSettingsStore _settingsStore;
        private readonly ISettingsSecretProtector _settingsSecretProtector;
        private readonly AntdUI.Segmented _settingsSegmented = new();
        private readonly AntdUI.Panel _settingsSegmentHost = new();
        private readonly Control[] _settingsSegments = new Control[6];
        private readonly TextBox _clientPath = new() { Dock = DockStyle.Fill };
        private readonly CheckBox _autoLaunchClient = new() { Text = "启动助手时直接启动 LOL 客户端", AutoSize = true };
        private readonly AntdUI.Label _clientStatus = new() { AutoSize = true, ForeColor = Color.DimGray };
        private readonly AntdUI.Button _launchClientButton = new() { Text = "立即启动 LOL", AutoSize = true };
        private readonly NumericUpDown _opacity = new() { Minimum = 40, Maximum = 100, Width = 130 };
        private readonly ComboBox _themeMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        private readonly TextBox _hotkey = new() { ReadOnly = true, Width = 160, TabStop = true };
        private readonly CheckBox _onlyLeagueFocused = new() { Text = "仅在 LOL 位于前台时响应", AutoSize = true };
        private QuickShoutForm? _quickShoutForm;
        private readonly CheckBox _recommendationEnabled = new() { Text = "启用 AI 时间线建议", AutoSize = true };
        private readonly CheckBox _opggBuildAssistantEnabled = new() { Text = "启用 OP.GG 选人出装与符文推荐", AutoSize = true };
        private readonly CheckBox _champSelectKdaAnnouncementEnabled = new() { Text = "选人加载完成后发送 KDA 评估到聊天", AutoSize = true };
        private readonly CheckBox _gameKdaAnnouncementEnabled = new() { Text = "对局中发送双方玩家近期 KDA 评估", AutoSize = true };
        private readonly CheckBox _gameKdaOnePlayerPerLine = new() { Text = "每名玩家单独发送一条（单人一行）", AutoSize = true };
        private readonly TextBox _champSelectKdaAnnouncementTemplate = new() { Dock = DockStyle.Fill, Multiline = true, Height = 86, ScrollBars = ScrollBars.Vertical };
        private readonly ComboBox _provider = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };

        // 可下拉可手填：模型名写错时服务端只回一个 400，所以按服务商给出可选值。
        private readonly ComboBox _model = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 290 };
        private readonly Button _testAi = new() { Text = "测试连接", AutoSize = true };
        private readonly Button _fetchModels = new() { Text = "获取可用模型", AutoSize = true };
        private readonly Label _aiTestStatus = new() { AutoSize = true, ForeColor = Color.DimGray };
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
            AttachCommonSegmentTips();
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
                AppCompositionRoot.ApplicationSettingsStore,
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
            GameMain? main = Program.GameMain;
            if (main is null || main.IsDisposed) return;

            main.ApplyWindowSettings(config);
            main.ApplyTheme();
            if (GameMain.liveGameForm is { IsDisposed: false } liveGameForm)
            {
                liveGameForm.ConfigureAutoRefresh(config.AutoRefresh,
                    Math.Max(10, config.AutoRefreshIntervalSeconds));
                liveGameForm.RefreshKdaAnnouncements();
            }
            main.ApplyRecommendationSettings(config);
            if (GameMain.coachForm is { IsDisposed: false } coachForm)
                coachForm.RefreshOpggAvailability();
        }

        /// <summary>
        /// “通用”分页由设计器生成，这里统一补上悬停说明。
        /// 只挂到该行的标题、开关与选择框本身：英雄头像预览另有一套显示名称的提示，
        /// 递归挂载会把那套提示覆盖掉。
        /// </summary>
        private void AttachCommonSegmentTips()
        {
            AttachTipDeep(label1, swi_open,
                "开启后，客户端回到大厅时助手会自动开始排队匹配；两次自动排队之间至少间隔 10 秒，避免重复触发。");
            AttachTipDeep(label2, swi_gametrue,
                "开启后，匹配到对手时自动点“接受”，不会因为没来得及点而退回队列。");
            AttachTipDeep(label3, swi_jyyx,
                "开启后在选人阶段自动禁用列表中的英雄，按“自动禁用间隔”反复尝试，直到禁用成功或选人结束。");
            AttachTipDeep(setting_select_jyx, "要自动禁用的英雄，可多选，按列表顺序尝试禁用。");
            AttachTip(flow_ban_preview, "已选禁用英雄的头像预览。");
            AttachTipDeep(label4, swi_xyx,
                "开启后在选人阶段立即抢选列表中的英雄；抢选不受“自动禁用间隔”影响，每 0.1 秒重试一次。");
            AttachTipDeep(setting_select_xyx, "要自动抢选的英雄，可多选，按列表顺序尝试选用。");
            AttachTip(flow_pick_preview, "已选抢选英雄的头像预览。");
            AttachTipDeep(label5, inputNumber1,
                "自动禁用循环两次尝试之间的间隔（秒）；不影响自动抢英雄的速度。");
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

        private Control CreateMatchSegment()
        {
            var panel = CreateSegmentPanel();
            var layout = CreateSegmentLayout();
            panel.Controls.Add(layout);
            int row = 0;
            AddSectionHeader(layout, row++, "匹配与对局");
            AddSegmentRow(layout, row++, "自动匹配：", swi_open);
            AddSegmentRow(layout, row++, "自动接受：", swi_gametrue);
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
            AddSegmentRow(layout, row++, "选用英雄列表：", setting_select_xyx);
            flow_pick_preview.Height = 96;
            AddSegmentRow(layout, row++, "选用预览：", flow_pick_preview);
            layout.SetColumnSpan(flow_pick_preview, 1);
            AddSegmentRow(layout, row++, "禁用间隔：", inputNumber1);
            AddSegmentRow(layout, row++, "说明：", CreateNote("本页的对局开关和英雄列表会自动保存；自动接受延迟、预选与补位策略在“客户端工具”中设置。"));
            return panel;
        }

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

        private Control CreateClientSegment()
        {
            var panel = CreateSegmentPanel();
            var layout = CreateSegmentLayout();
            panel.Controls.Add(layout);

            var browse = new AntdUI.Button { Text = "选择安装文件夹", AutoSize = true, Dock = DockStyle.Right };
            browse.Click += (_, _) => BrowseClientExecutable();
            var pathPanel = new Panel { Dock = DockStyle.Fill, Height = 32 };
            pathPanel.Controls.Add(_clientPath);
            pathPanel.Controls.Add(browse);

            _launchClientButton.Click += async (_, _) => await StartLeagueClientFromSettingsAsync();
            var note = CreateNote("选择 LOL 安装文件夹后，助手会查找 LeagueClient.exe，并按 Riot 安装清单中的分支启动；国服可能还需要在 Riot/WeGame 启动器完成登录或更新。\n也可留空，由助手从安装清单和常见位置查找。自动启动只在助手打开时执行一次。");

            AddSegmentRow(layout, 0, "安装文件夹：", pathPanel,
                "LOL 安装目录。助手会在其子目录中查找 LeagueClient.exe，用于“立即启动”和启动助手时的自动启动；留空则尝试常见安装位置。");
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

        private Control CreateWindowSegment()
        {
            var panel = CreateSegmentPanel();
            var layout = CreateSegmentLayout();
            panel.Controls.Add(layout);

            var opacityPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            opacityPanel.Controls.Add(_opacity);
            opacityPanel.Controls.Add(new Label { Text = "%（40–100）", AutoSize = true, Padding = new Padding(6, 6, 0, 0) });
            _hotkey.KeyDown += CaptureHoldToTopHotkey;
            _hotkey.Click += (_, _) => _hotkey.Focus();
            _hotkey.Enter += (_, _) => Program.GameMain.SetWindowHotkeyCapturePaused(true);
            _hotkey.Leave += (_, _) => Program.GameMain.SetWindowHotkeyCapturePaused(false);
            _themeMode.Items.AddRange(new object[] { "跟随系统", "浅色", "深色" });
            var holdNote = CreateNote("点击输入框后按一个按键。按住该键时助手会以不抢焦点的方式临时置顶，松开后隐藏到托盘；默认是键盘左上角的 · 键。\nWindows 会接管已注册的按键；快捷键不会注入或修改游戏客户端。");

            AddSegmentRow(layout, 0, "界面主题：", _themeMode,
                "界面配色：跟随系统、浅色或深色，保存后立即生效。");
            AddSegmentRow(layout, 1, "窗口透明度：", opacityPanel,
                "助手窗口的不透明度（40–100），数值越小越透明。");
            AddSegmentRow(layout, 2, "按住置顶键：", _hotkey,
                "点击输入框后按一个键即可设定。按住该键时助手临时置顶且不抢焦点，松开后隐藏到托盘。");
            AddSegmentRow(layout, 3, "快捷键范围：", _onlyLeagueFocused,
                "开启时只有 LOL 位于前台才响应置顶键；关闭则任何窗口下都响应。");
            AddSegmentRow(layout, 4, "置顶说明：", holdNote);
            AddSectionHeader(layout, 5, "窗口行为");
            AddSegmentRow(layout, 6, "最小化到托盘：", swi_tray);
            AddSegmentRow(layout, 7, "游戏分辨率：", select_resolution);
            AddSaveRow(layout, 8, "保存窗口设置");
            return panel;
        }

        private Control CreateShoutSegment()
        {
            _quickShoutForm = new QuickShoutForm(AppCompositionRoot.QuickShoutService,
                (phrase, sendToAll, useClipboard, perCharacter, interval) =>
                    Program.GameMain.SendQuickShoutToGameAsync(phrase, sendToAll, useClipboard,
                        perCharacter, interval),
                () => Program.GameMain.TestGameChatOpenAsync(),
                SaveShoutSettings);
            _quickShoutForm.LoadSettings(_settingsStore.Load());
            return _quickShoutForm;
        }

        public Task SendRandomQuickShoutToGameAsync(bool custom) =>
            _quickShoutForm?.SendRandomToGameAsync(custom) ?? Task.CompletedTask;

        private void SaveShoutSettings()
        {
            if (_isLoading || _quickShoutForm is null) return;
            AssistantSettings latest = _settingsStore.Load();
            _quickShoutForm.WriteSettings(latest);
            if (latest.QuickShoutHotkeysEnabled &&
                (latest.QuickShoutBuiltInHotkey.Equals(latest.HoldToTopHotkey, StringComparison.OrdinalIgnoreCase) ||
                 latest.QuickShoutCustomHotkey.Equals(latest.HoldToTopHotkey, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("喊话快捷键不能与按住置顶键相同。 ");
            _settingsStore.Save(latest);
            _config = latest;
            Program.GameMain.ConfigureQuickShoutHotkeys(latest);
        }

        private Control CreateAiSegment()
        {
            var panel = CreateSegmentPanel();
            var layout = CreateSegmentLayout();
            panel.Controls.Add(layout);

            _gameKdaAnnouncementEnabled.CheckedChanged += (_, _) =>
                _gameKdaOnePlayerPerLine.Enabled = _gameKdaAnnouncementEnabled.Checked;

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
            _testAi.Click += async (_, _) => await TestAiConnectionAsync();
            providerPanel.Controls.Add(_testAi);
            _fetchModels.Click += async (_, _) => await FetchAiModelsAsync();
            providerPanel.Controls.Add(_fetchModels);

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
            var overlayNote = CreateNote("浮窗背景完全透明，只显示带深色描边的文字，不遮挡游戏画面；默认显示在游戏左下角，不抢键盘焦点；横向/纵向偏移以所选角落为基准。关闭此项后，建议只会更新到“智能建议”页。 ");
            var privacyNote = CreateNote("隐私说明：启用 AI 时间线建议并主动保存后，当前英雄、游戏阶段、可见阵容、游戏时间、金币与已购装备会发送给所选 AI 服务商以生成建议；不会发送 LCU Token、账号密码、玩家身份或本机聊天内容。未配置 API Key 或模型时不会发送数据，也不会显示替代建议。");
            var opggNote = CreateNote("开启后，助手会在选人阶段检测到你已选定英雄时弹出图文方案。选中并点击应用后，才会从 OP.GG 读取公开推荐，并写入本机客户端的符文页与自定义物品集；取消不会修改任何内容。若自定义符文页已满，会就地改写当前正在使用的符文页（不删除任何页面），不会清理其它自定义页。");
            var kdaAnnouncementNote = CreateNote("选人发送只汇总我方，通过客户端群聊发送一次；对局发送默认蓝方、红方各一条。勾选“单人一行”后，每名玩家各发送一条，标准 5v5 共 10 条，消息会更紧凑。对局发送沿用喊话页的游戏内发送方式与“所有人”选项。两者最多统计最近同队列的 20 场已结束对局，满 8 场按累计 KDA 分档；不足 8 场标“样本不足”。若玩家资料暂不可查，会在消息中标注。选人模板支持 {players}、{allies}。");

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
                "游戏进行中双方玩家的近期 KDA 评估加载完成后，按喊话页设置发送到游戏聊天，一局一次；会包含敌方玩家。");
            AddSegmentRow(layout, 6, "对局发送排版：", _gameKdaOnePlayerPerLine,
                "勾选后每名玩家各发一条，游戏聊天中每人占一行；不勾选时蓝方、红方各发一条。仅在“对局 KDA 发送”开启时生效。");
            AddSegmentRow(layout, 7, "KDA 说明：", kdaAnnouncementNote);
            AddSegmentRow(layout, 8, "服务商：", providerPanel,
                "这一行有三个按钮：“获取 API Key”打开服务商密钥页；“获取可用模型”用当前密钥读取服务端支持的模型名；“测试连接”用当前填写的服务商、模型与密钥发一次最小请求。");
            AddSegmentRow(layout, 9, "模型名称：", _model,
                "要调用的模型名，建议先点“获取可用模型”再从这里选。模型名由服务商决定，写错时只会得到 400。");
            AddSegmentRow(layout, 10, "接口地址：", _baseUrl,
                "API 基础地址；切换服务商时会自动填好，使用默认地址时不用改。");
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
            AddSaveRow(layout, 21, "保存 AI 设置");
            return panel;
        }

        // AntdUI.Panel 自身不提供滚动条；此处仅作为滚动内容承载容器，内部控件仍使用 AntdUI。
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

        private static System.Windows.Forms.Label CreateNote(string text) => new()
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
                control.Height = control is AntdUI.SelectMultiple ? 48 : 34;
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
        private void AttachTip(Control control, string text) => _featureTip.SetToolTip(control, text);

        /// <summary>
        /// 给控件及其所有子控件挂同一段说明。设置了说明的控件多为容器（面板、下拉框、按钮组），
        /// 只有递归下去，鼠标落在里面真正的输入控件上才会显示。
        /// </summary>
        private void AttachTipDeep(Control control, string text)
        {
            _featureTip.SetToolTip(control, text);
            foreach (Control child in control.Controls) AttachTipDeep(child, text);
        }

        private void AttachTipDeep(Control caption, Control control, string text)
        {
            AttachTip(caption, text);
            AttachTipDeep(control, text);
        }

        private void AddSaveRow(TableLayoutPanel layout, int row, string text)
        {
            var save = new AntdUI.Button { Text = text, AutoSize = true };
            save.Click += (_, _) => SaveExtendedSettings();
            AddSegmentRow(layout, row, "", save, $"点击后立即生效，并写入本机的 {_settingsStore.GetStoragePath()}");
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
            _quickShoutForm?.LoadSettings(_config);

            CloudAiSettings ai = _config.Ai;
            _recommendationEnabled.Checked = ai.RecommendationEnabled;
            _opggBuildAssistantEnabled.Checked = _config.OpggBuildAssistantEnabled;
            _champSelectKdaAnnouncementEnabled.Checked = _config.ChampSelectKdaAnnouncementEnabled;
            _gameKdaAnnouncementEnabled.Checked = _config.GameKdaAnnouncementEnabled;
            _gameKdaOnePlayerPerLine.Checked = _config.GameKdaOnePlayerPerLine;
            _gameKdaOnePlayerPerLine.Enabled = _config.GameKdaAnnouncementEnabled;
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

            CloudAiSettings ai = _config.Ai;
            ai.RecommendationEnabled = _recommendationEnabled.Checked;
            _config.OpggBuildAssistantEnabled = _opggBuildAssistantEnabled.Checked;
            _config.ChampSelectKdaAnnouncementEnabled = _champSelectKdaAnnouncementEnabled.Checked;
            _config.GameKdaAnnouncementEnabled = _gameKdaAnnouncementEnabled.Checked;
            _config.GameKdaOnePlayerPerLine = _gameKdaOnePlayerPerLine.Checked;
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

            if (result.IsReady) AntdUI.Message.success(Program.GameMain, result.Message);
            else if (result.Started) AntdUI.Message.info(Program.GameMain, result.Message);
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

        private void SetAiTestStatus(string text, Color color)
        {
            _aiTestStatus.ForeColor = color;
            _aiTestStatus.Text = text;
        }

        /// <summary>按界面上当前填写的值组装一份设置，供“测试连接”和“获取可用模型”共用。</summary>
        private CloudAiSettings BuildAiSettingsFromUi() => new()
        {
            Provider = _provider.SelectedItem is LOL_GameAssistant.Domain.Settings.AiProvider provider
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
