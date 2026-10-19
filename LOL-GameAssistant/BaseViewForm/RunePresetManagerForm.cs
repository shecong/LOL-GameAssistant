using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.Builds;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.LeagueClient;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>从客户端当前符文页与召唤师技能保存个人方案，按英雄及场景维护优先级。</summary>
internal sealed class RunePresetManagerForm : AntdUI.Window, IThemeAware
{
    private readonly IGameAssetService _assets;
    private readonly IApplicationSettingsStore _settings;
    private readonly IOpggBuildApplyService _builds;
    private readonly IAiCoachingService _context;
    private readonly IChampionCatalog _champions = AppCompositionRoot.ChampionCatalog;
    private readonly AntdUI.Table _list = new() { Dock = DockStyle.Fill, VisibleHeader = false, Bordered = true };
    private readonly AntdUI.Input _name = new() { Name = "presetName", Width = 400, Height = 40 };
    private readonly AntdUI.Select _mode = new() { ReadOnly = true, Width = 155, Height = 40 };
    private readonly AntdUI.Select _position = new() { ReadOnly = true, Width = 125, Height = 40 };
    private readonly AntdUI.InputNumber _priority = new() { Minimum = -100, Maximum = 100, Width = 80, Height = 36 };
    private readonly AntdUI.Checkbox _auto = new() { Text = "选人时自动使用", AutoSize = true };
    private readonly AntdUI.Input _items = new() { Width = 420, Height = 90, Multiline = true, PlaceholderText = "每行：装备组名称:物品ID,物品ID；留空仅应用符文和技能" };
    private readonly AntdUI.Label _detail = new() { AutoSize = true, MaximumSize = new Size(400, 0) };
    private readonly AntdUI.Label _status = new() { Dock = DockStyle.Bottom, Height = 42, TextAlign = ContentAlignment.MiddleLeft };
    private readonly AntdUI.Segmented _categories = new() { Dock = DockStyle.Top, Height = 260, Vertical = true, Full = false, SelectIndex = 0 };
    private readonly FlowLayoutPanel _runePreview = new() { Width = 440, Height = 190, AutoScroll = true, WrapContents = true };
    private readonly ToolTip _tips = new() { AutoPopDelay = 20000 };
    private readonly CancellationTokenSource _lifetime = new();
    private int _detailVersion;
    private IReadOnlyList<PersonalRunePreset> _clientPages = [];
    private List<PersonalRunePreset> _presets = new();
    private readonly int? _championFilter;
    /// <summary>个人符文方案列表使用的显示行。</summary>
    private sealed record PresetRow(string Label);
    /// <summary>设置选择项的协议键及中英文显示名称。</summary>
    private sealed record Choice(string Key, string Chinese, string English)
    {
        /// <summary>返回当前条目用于文本控件展示的内容。</summary>
        public override string ToString() => UiLanguage.IsEnglish ? English : Chinese;
    }
    private static readonly Choice[] Modes =
    [
        new("ranked", "召唤师峡谷", "Summoner's Rift"), new("aram", "极地大乱斗", "ARAM"),
        new("arena", "斗魂竞技场", "Arena"), new("urf", "无限火力", "URF"),
        new("nexus_blitz", "极限闪击", "Nexus Blitz")
    ];
    private static readonly Choice[] Positions =
    [
        new("top", "上路", "Top"), new("jungle", "打野", "Jungle"),
        new("mid", "中路", "Mid"), new("adc", "下路", "Bottom"),
        new("support", "辅助", "Support"), new("unknown", "不限", "Any")
    ];

    /// <summary>初始化 RunePresetManagerForm 的实例状态，并保存传入的依赖或数据。</summary>
    public RunePresetManagerForm(IApplicationSettingsStore settings, IOpggBuildApplyService builds, IAiCoachingService context, IGameAssetService? assets = null, int? championId = null)
    {
        _championFilter = championId;
        _assets = assets ?? AppCompositionRoot.GameAssetService;
        _settings = settings;
        _builds = builds;
        _context = context;
        Text = "我的符文方案";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 500);
        Size = new Size(1040, 720);
        Font = new Font("Microsoft YaHei UI", 9);
        _mode.Items.AddRange(Modes);
        _position.Items.AddRange(Positions);
        _list.Columns.Add(new AntdUI.Column(nameof(PresetRow.Label), "方案") { Width = "100%" });

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        var library = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 14, 0) };
        foreach (string category in new[] { "全部方案", "应用记录", "自动方案", "客户端页面" })
            _categories.Items.Add(new AntdUI.SegmentedItem { Text = category });
        _categories.SelectIndexChanged += (_, _) => Reload();
        library.Controls.Add(_list);
        library.Controls.Add(_categories);
        root.Controls.Add(library, 0, 0);
        var editor = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        editor.Controls.Add(new AntdUI.Label { Text = "本地符文库 · 已应用的推荐会自动保存在这里", AutoSize = true });
        editor.Controls.Add(new AntdUI.Label { Text = "方案名称", AutoSize = true });
        editor.Controls.Add(_name);
        editor.Controls.Add(new AntdUI.Label { Text = "模式", AutoSize = true });
        editor.Controls.Add(_mode);
        editor.Controls.Add(new AntdUI.Label { Text = "推荐分路（仅自动使用时匹配）", AutoSize = true });
        editor.Controls.Add(_position);
        editor.Controls.Add(new AntdUI.Label { Text = "优先级（数值越高越先使用）", AutoSize = true });
        editor.Controls.Add(_priority);
        editor.Controls.Add(_auto);
        editor.Controls.Add(new AntdUI.Label { Text = "个人装备块（应用前可预览修改范围）", AutoSize = true });
        editor.Controls.Add(_items);
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Width = 385 };
        var capture = new AntdUI.Button { Text = "从当前选人捕获", AutoSize = true };
        var save = new AntdUI.Button { Text = "保存修改", AutoSize = true };
        var apply = new AntdUI.Button { Text = "立即应用", AutoSize = true };
        var delete = new AntdUI.Button { Text = "删除方案", AutoSize = true };
        var undo = new AntdUI.Button { Text = "恢复应用前配置", AutoSize = true };
        undo.Click += async (_, _) => { try { var result = await _builds.RestoreLastConfigurationAsync(_lifetime.Token); if (!IsDisposed) _status.Text = result.Message; } catch (OperationCanceledException) { } catch (Exception e) { if (!IsDisposed) _status.Text = e.Message; } };
        actions.Controls.AddRange(new Control[] { capture, save, apply, delete, undo });
        editor.Controls.Add(actions);
        editor.Controls.Add(_detail);
        editor.Controls.Add(_runePreview);
        editor.SizeChanged += (_, _) =>
        {
            _name.Width = _items.Width = actions.Width = Math.Max(220, editor.ClientSize.Width - 28);
            _runePreview.Width = Math.Max(220, editor.ClientSize.Width - 28);
        };
        root.Controls.Add(editor, 1, 0);
        Controls.Add(root);
        Controls.Add(_status);
        AntdWindowChrome.AddHeader(this);
        _list.SelectIndexChanged += (_, _) => ShowSelected();
        capture.Click += async (_, _) => await CaptureAsync();
        save.Click += (_, _) => SaveSelected();
        apply.Click += async (_, _) => await ApplySelectedAsync();
        delete.Click += (_, _) => DeleteSelected();
        Reload();
        Shown += async (_, _) =>
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                _clientPages = await _builds.GetLocalRunePagesAsync(timeout.Token);
                if (!IsDisposed) Reload(Selected?.Id);
            }
            catch (OperationCanceledException) { }
            catch { if (!IsDisposed) _status.Text = "客户端页面暂不可用；本地保存的方案仍可浏览。"; }
        };
        UiLanguage.Changed += LanguageChanged;
        Disposed += (_, _) =>
        {
            UiLanguage.Changed -= LanguageChanged;
            _lifetime.Cancel();
            _lifetime.Dispose();
            _tips.Dispose();
            ClearPreview();
        };
        UiTheme.Apply(this);
    }

    public void ApplyTheme(ThemePalette palette)
    {
        BackColor = palette.Surface;
        ForeColor = palette.TextPrimary;
        _categories.BackColor = _list.BackColor = palette.SurfaceRaised;
        _categories.ForeColor = palette.TextPrimary;
        _status.ForeColor = palette.TextSecondary;
        _runePreview.BackColor = palette.SurfaceRaised;
    }

    /// <summary>语言变化后刷新依赖本地化文本的界面。</summary>
    private void LanguageChanged(object? sender, EventArgs args)
    {
        _mode.Refresh();
        _position.Refresh();
        Reload(Selected?.Id);
    }

    /// <summary>返回当前选中的个人符文方案，无有效选择时返回空。</summary>
    private PersonalRunePreset? Selected => _list.SelectedIndex is int index && index >= 0 && index < _presets.Count
        ? _presets[index] : null;

    /// <summary>重新读取符文方案并恢复有效的选择。</summary>
    private void Reload(Guid? selectId = null)
    {
        var saved = _settings.Load().PersonalRunePresets;
        _presets = (_categories.SelectIndex == 3 ? _clientPages : saved.AsEnumerable())
            .Where(preset => _categories.SelectIndex == 3 || !_championFilter.HasValue || preset.ChampionId == _championFilter)
            .Where(preset => _categories.SelectIndex != 1 || preset.LastAppliedAt.HasValue)
            .Where(preset => _categories.SelectIndex != 2 || preset.AutoApply)
            .OrderByDescending(preset => preset.LastAppliedAt).ThenBy(preset => preset.ChampionId).ThenByDescending(preset => preset.Priority).ToList();
        _list.DataSource = _presets.Select(preset =>
        {
            if (preset.Source == "客户端符文页") return new PresetRow($"{preset.Name} · {preset.RunePerkIds.Count} 个符文");
            string champion = _champions.GetDisplayName(preset.ChampionId);
            if (string.IsNullOrWhiteSpace(champion)) champion = $"#{preset.ChampionId}";
            return new PresetRow($"{preset.Source} · {champion} · {preset.Name} · {ModeLabel(preset.Mode)}/{PositionLabel(preset.Position)}" +
                (preset.AutoApply ? (UiLanguage.IsEnglish ? " · Auto" : " · 自动") : ""));
        }).ToArray();
        int index = selectId.HasValue ? _presets.FindIndex(preset => preset.Id == selectId.Value) : -1;
        if (_presets.Count > 0) _list.SelectedIndex = index >= 0 ? index : 0;
        ShowSelected();
        if (_presets.Count == 0) _status.Text = "暂无本地方案。成功应用推荐后会自动记录，也可从当前选人捕获。";
    }

    /// <summary>将选中符文方案的字段同步到编辑区域。</summary>
    private void ShowSelected()
    {
        var preset = Selected;
        if (preset == null) { ClearPreview(); _detail.Text = ""; return; }
        _name.Text = preset.Name;
        _mode.SelectedValue = Modes.FirstOrDefault(item => item.Key == preset.Mode);
        _position.SelectedValue = Positions.FirstOrDefault(item => item.Key == preset.Position);
        _priority.Value = Math.Clamp(preset.Priority, -100, 100);
        _auto.Checked = preset.AutoApply;
        _items.Text = string.Join(Environment.NewLine, (preset.ItemBlocks ?? []).Select(b => b.Title + ":" + string.Join(",", b.ItemIds)));
        _detail.Text = preset.Source == "客户端符文页" ? "客户端现有符文页 · 可查看符文名称与效果" :
            $"{preset.Source} · {ModeLabel(preset.Mode)} / {PositionLabel(preset.Position)}\n" +
            (preset.LastAppliedAt is { } applied ? $"最近应用：{applied.LocalDateTime:yyyy-MM-dd HH:mm}" : "尚无应用记录");
        _ = ShowRuneDetailsAsync(preset);

    }

    private void ClearPreview()
    {
        foreach (Control control in _runePreview.Controls.Cast<Control>().ToArray())
        {
            foreach (PictureBox picture in control.Controls.OfType<PictureBox>()) picture.Image?.Dispose();
            control.Dispose();
        }
        _runePreview.Controls.Clear();
    }

    private async Task ShowRuneDetailsAsync(PersonalRunePreset preset)
    {
        int version = ++_detailVersion;
        ClearPreview();
        var assets = _assets;
        using var gate = new SemaphoreSlim(4);
        var tasks = preset.RunePerkIds.Select(async id =>
        {
            await gate.WaitAsync(_lifetime.Token);
            try
            {
                var textTask = assets.GetRuneDetailsAsync(id, _lifetime.Token);
                var imageTask = assets.GetRuneIconAsync(id, _lifetime.Token);
                await Task.WhenAll(textTask, imageTask);
                if (IsDisposed || version != _detailVersion) return;
                string detail = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(textTask.Result ?? $"符文 {id}", "<[^>]+>", ""));
                var tile = new Panel { Size = new Size(98, 78), Margin = new Padding(0, 0, 6, 8) };
                var image = new PictureBox { Dock = DockStyle.Top, Height = 46, SizeMode = PictureBoxSizeMode.Zoom };
                if (imageTask.Result is { IsEmpty: false } asset)
                {
                    using var stream = new MemoryStream(asset.Content);
                    using var original = Image.FromStream(stream);
                    image.Image = new Bitmap(original);
                }
                var name = new Label { Dock = DockStyle.Bottom, Height = 28, Text = detail.Split('\n')[0], TextAlign = ContentAlignment.MiddleCenter, AutoEllipsis = true };
                tile.Controls.Add(image); tile.Controls.Add(name);
                _tips.SetToolTip(image, detail); _tips.SetToolTip(name, detail);
                _runePreview.Controls.Add(tile);
            }
            catch (OperationCanceledException) { }
            catch { /* 静态资源失败不影响本地方案。 */ }
            finally { gate.Release(); }
        });
        try { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) { }
    }

    /// <summary>从选人阶段的当前符文页和技能捕获个人方案。</summary>
    private async Task CaptureAsync()
    {
        try
        {
            var context = await _context.CollectContextAsync();
            if (context.Phase != "ChampSelect" || context.MyChampionId <= 0)
            { _status.Text = "请先在选人阶段选定英雄并配置好符文与召唤师技能。"; return; }
            string mode = OpggBuildApplyService.NormalizeMode(context.GameMode, context.QueueId);
            if (mode == "aram_mayhem")
            { _status.Text = "海克斯大乱斗没有常规符文页；请使用该模式的专属出装与增幅推荐。"; return; }
            var preset = await _builds.CaptureCurrentRunePresetAsync(context.MyChampionId, mode, context.MyRole);
            if (preset == null) { _status.Text = "无法读取完整的当前符文页或召唤师技能。"; return; }
            preset.Name = $"{context.MyChampion} · {mode}";
            preset.AutoApply = true;
            var settings = _settings.Load();
            settings.PersonalRunePresets.Add(preset);
            _settings.Save(settings);
            Reload(preset.Id);
            _status.Text = "已捕获方案；可修改名称、场景和优先级。";
        }
        catch (Exception ex) { _status.Text = $"捕获失败：{ex.Message}"; }
    }

    /// <summary>验证编辑内容并保存选中的个人符文方案。</summary>
    private void SaveSelected()
    {
        var preset = Selected;
        if (preset == null) return;
        string name = _name.Text.Trim();
        if (name.Length is < 1 or > 80) { _status.Text = "方案名称需为 1 至 80 个字符。"; return; }
        var settings = _settings.Load();
        var target = settings.PersonalRunePresets.FirstOrDefault(item => item.Id == preset.Id);
        if (target == null) { _status.Text = "此项为客户端页面预览。请在选人时捕获后再保存为个人方案。"; return; }
        var blocks = new List<PersonalItemBlock>();
        foreach (string line in (_items.Text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = line.Trim().Split(':', 2); if (pieces.Length != 2 || string.IsNullOrWhiteSpace(pieces[0])) { _status.Text = "装备块格式：名称:物品ID,物品ID"; return; }
            var ids = new List<int>(); foreach (string item in pieces[1].Split(',', StringSplitOptions.RemoveEmptyEntries)) { if (!int.TryParse(item.Trim(), out int id) || id <= 0) { _status.Text = "装备 ID 必须为正整数"; return; } ids.Add(id); }
            if (ids.Count is 0 or > 10 || blocks.Count >= 12) { _status.Text = "每组需有 1～10 件装备，最多 12 组"; return; }
            blocks.Add(new PersonalItemBlock(pieces[0].Trim(), ids));
        }
        target.Name = name;
        target.Mode = (_mode.SelectedValue as Choice)?.Key ?? target.Mode;
        target.Position = (_position.SelectedValue as Choice)?.Key ?? target.Position;
        target.ItemBlocks = blocks;
        target.Priority = (int)_priority.Value;
        target.AutoApply = _auto.Checked;
        _settings.Save(settings);
        Reload(target.Id);
        _status.Text = "方案已保存。";
    }

    /// <summary>确认英雄和游戏阶段后应用选中的个人方案。</summary>
    private async Task ApplySelectedAsync()
    {
        var preset = Selected;
        if (preset == null) return;
        if (preset.Source == "客户端符文页") { _status.Text = "客户端页面仅供查看；请从当前选人捕获后应用。"; return; }
        var context = await _context.CollectContextAsync();
        if (context.Phase != "ChampSelect" || context.MyChampionId != preset.ChampionId)
        {
            _status.Text = UiLanguage.IsEnglish
                ? "Select this preset's champion during champion select before applying it."
                : "请在选人阶段选用该方案对应英雄后再应用。";
            return;
        }
        var result = await _builds.ApplyPersonalRunePresetAsync(preset);
        _status.Text = result.Message;
        if (result.Succeeded)
        {
            var saved = _settings.Load();
            var applied = saved.PersonalRunePresets.FirstOrDefault(item => item.Id == preset.Id);
            if (applied != null) { applied.LastAppliedAt = DateTimeOffset.Now; _settings.Save(saved); Reload(applied.Id); }
        }
    }

    /// <summary>将方案模式键转换为当前语言的名称。</summary>
    private static string ModeLabel(string mode) => mode switch
    {
        "ranked" => UiLanguage.IsEnglish ? "Summoner's Rift" : "召唤师峡谷",
        "aram" => UiLanguage.IsEnglish ? "ARAM" : "极地大乱斗",
        "arena" => UiLanguage.IsEnglish ? "Arena" : "斗魂竞技场",
        "urf" => UiLanguage.IsEnglish ? "URF" : "无限火力",
        "nexus_blitz" => UiLanguage.IsEnglish ? "Nexus Blitz" : "极限闪击",
        _ => mode
    };

    /// <summary>将方案分路键转换为当前语言的名称。</summary>
    private static string PositionLabel(string position) => position switch
    {
        "top" => UiLanguage.IsEnglish ? "Top" : "上路",
        "jungle" => UiLanguage.IsEnglish ? "Jungle" : "打野",
        "mid" => UiLanguage.IsEnglish ? "Mid" : "中路",
        "adc" => UiLanguage.IsEnglish ? "Bottom" : "下路",
        "support" => UiLanguage.IsEnglish ? "Support" : "辅助",
        _ => UiLanguage.IsEnglish ? "Any" : "不限"
    };

    /// <summary>删除当前选中的个人符文方案并刷新列表。</summary>
    private void DeleteSelected()
    {
        var preset = Selected;
        if (preset == null) return;
        var settings = _settings.Load();
        settings.PersonalRunePresets.RemoveAll(item => item.Id == preset.Id);
        _settings.Save(settings);
        Reload();
        _status.Text = "方案已删除。";
    }
}
