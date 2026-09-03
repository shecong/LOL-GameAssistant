using LOL_GameAssistant.Entity;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.LoLApi;
using Microsoft.Win32;

namespace LOL_GameAssistant.BaseViewForm
{
    public partial class SettingForm : UserControl
    {
        public DateTime? lastOpenGameTime = null;
        private SettingConfig _config;
        private bool _isLoading;
        private ToolTip toolTip1 = new ToolTip();
        private static readonly string[] ResolutionPresets = { "1280x720", "1366x768", "1600x900", "1920x1080", "2560x1440", "3840x2160" };

        public SettingForm()
        {
            InitializeComponent();
            _config = new SettingConfig();
        }

        private async void SettingForm_Load(object sender, EventArgs e)
        {
            await LoadCachedSettings();
            await LoadBase();
            ApplySideEffects(_config);
        }

        #region 本地缓存

        private async Task LoadCachedSettings()
        {
            _isLoading = true;
            _config = SettingCache.Load();

            label_cache_status.Text = $"缓存文件: {SettingCache.GetCacheFilePath()}";

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

            SettingCache.Save(_config);
            ApplySideEffects(_config);
            label_cache_status.Text = $"已缓存: {SettingCache.GetCacheFilePath()}";
        }

        /// <summary>
        /// 将需要立即生效的设置同步到系统或其它模块。
        /// </summary>
        private static void ApplySideEffects(SettingConfig config)
        {
            ApplyStartupSetting(config.LaunchOnStartup);
            GameMain.liveGameForm.ConfigureAutoRefresh(
                config.AutoRefresh,
                Math.Max(10, config.AutoRefreshIntervalSeconds));
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
                    key.SetValue("LOLGameAssistant", $"\"{Application.ExecutablePath}\"");
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
                var stream = await Game_Api.GetGameYXImg(championId);
                if (stream == null || stream == Stream.Null) return null;
                var img = Image.FromStream(stream);
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
                var name = ChampionMap.GetChampion(id)?.RealName ?? "?";
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
        #endregion

        #region 定时执行方法

        public static void OpenGame(SettingForm form)
        {
            var now = DateTime.Now;
            var lastOpen = form.lastOpenGameTime;
            // 直接读取持久化配置，避免“设置页从未打开时 _config 未加载”导致自动匹配不生效
            if ((lastOpen == null || (now - lastOpen.Value).TotalSeconds >= 10) && SettingCache.Load().AutoMatch)
            {
                _ = Game_Api.OpenGameServer();
                form.lastOpenGameTime = now;
            }
        }

        public static void GameTrue(SettingForm form)
        {
            if (SettingCache.Load().AutoAccept)
            {
                _ = Game_Api.GameTrueServer();
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
            var result = new List<int>();
            var map = ChampionMap.GetChampionMap();
            foreach (var name in _config.BanChampions)
            {
                foreach (var entry in map)
                {
                    if (string.Equals(entry.Value.RealName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(entry.Key);
                        break;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// 获取设置中选定的选用英雄ID列表
        /// </summary>
        public List<int> GetPickChampionIds()
        {
            var result = new List<int>();
            var map = ChampionMap.GetChampionMap();
            foreach (var name in _config.PickChampions)
            {
                foreach (var entry in map)
                {
                    if (string.Equals(entry.Value.RealName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(entry.Key);
                        break;
                    }
                }
            }
            return result;
        }

        
        #endregion
        #endregion

        private async Task LoadBase()
        {
            var allChampions = ChampionMap.GetChampionMap();
            for (int i = 0; i < allChampions.Count; i++)
            {
                this.setting_select_jyx.Items.Add(allChampions.ElementAt(i).Value.RealName);
                this.setting_select_xyx.Items.Add(allChampions.ElementAt(i).Value.RealName);
            }
            RestoreSelectedChampions();
        }

        private void RestoreSelectedChampions()
        {
            SetSelectedTexts(setting_select_jyx, _config.BanChampions);
            SetSelectedTexts(setting_select_xyx, _config.PickChampions);
        }
    }
}
