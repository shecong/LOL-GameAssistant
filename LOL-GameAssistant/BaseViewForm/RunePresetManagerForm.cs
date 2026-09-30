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
internal sealed class RunePresetManagerForm : Form
{
    private readonly IApplicationSettingsStore _settings;
    private readonly IOpggBuildApplyService _builds;
    private readonly IAiCoachingService _context;
    private readonly IChampionCatalog _champions = AppCompositionRoot.ChampionCatalog;
    private readonly AntdUI.Table _list = new() { Dock = DockStyle.Fill, VisibleHeader = false, Bordered = true };
    private readonly AntdUI.Input _name = new() { Width = 250 };
    private readonly AntdUI.Select _mode = new() { ReadOnly = true, Width = 155 };
    private readonly AntdUI.Select _position = new() { ReadOnly = true, Width = 125 };
    private readonly AntdUI.InputNumber _priority = new() { Minimum = -100, Maximum = 100, Width = 80 };
    private readonly AntdUI.Checkbox _auto = new() { Text = "选人时自动使用", AutoSize = true };
    private readonly AntdUI.Label _detail = new() { AutoSize = true, MaximumSize = new Size(400, 0) };
    private readonly AntdUI.Label _status = new() { Dock = DockStyle.Bottom, Height = 42, TextAlign = ContentAlignment.MiddleLeft };
    private List<PersonalRunePreset> _presets = new();
    private sealed record PresetRow(string Label);
    private sealed record Choice(string Key, string Chinese, string English)
    {
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

    public RunePresetManagerForm(IApplicationSettingsStore settings, IOpggBuildApplyService builds, IAiCoachingService context)
    {
        _settings = settings;
        _builds = builds;
        _context = context;
        Text = "我的符文方案";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 500);
        Size = new Size(820, 550);
        Font = new Font("Microsoft YaHei UI", 9);
        _mode.Items.AddRange(Modes);
        _position.Items.AddRange(Positions);
        _list.Columns.Add(new AntdUI.Column(nameof(PresetRow.Label), "方案") { Width = "100%" });

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        root.Controls.Add(_list, 0, 0);
        var editor = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        editor.Controls.Add(new AntdUI.Label { Text = "选中方案；从当前客户端符文页和技能捕获新方案", AutoSize = true });
        editor.Controls.Add(new AntdUI.Label { Text = "方案名称", AutoSize = true });
        editor.Controls.Add(_name);
        editor.Controls.Add(new AntdUI.Label { Text = "模式", AutoSize = true });
        editor.Controls.Add(_mode);
        editor.Controls.Add(new AntdUI.Label { Text = "分路（峡谷模式精确匹配）", AutoSize = true });
        editor.Controls.Add(_position);
        editor.Controls.Add(new AntdUI.Label { Text = "优先级（数值越高越先使用）", AutoSize = true });
        editor.Controls.Add(_priority);
        editor.Controls.Add(_auto);
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Width = 385 };
        var capture = new AntdUI.Button { Text = "从当前选人捕获", AutoSize = true };
        var save = new AntdUI.Button { Text = "保存修改", AutoSize = true };
        var apply = new AntdUI.Button { Text = "立即应用", AutoSize = true };
        var delete = new AntdUI.Button { Text = "删除方案", AutoSize = true };
        actions.Controls.AddRange(new Control[] { capture, save, apply, delete });
        editor.Controls.Add(actions);
        editor.Controls.Add(_detail);
        root.Controls.Add(editor, 1, 0);
        Controls.Add(root);
        Controls.Add(_status);
        _list.SelectIndexChanged += (_, _) => ShowSelected();
        capture.Click += async (_, _) => await CaptureAsync();
        save.Click += (_, _) => SaveSelected();
        apply.Click += async (_, _) => await ApplySelectedAsync();
        delete.Click += (_, _) => DeleteSelected();
        Reload();
        UiLanguage.Changed += LanguageChanged;
        Disposed += (_, _) => UiLanguage.Changed -= LanguageChanged;
        UiTheme.Apply(this);
    }

    private void LanguageChanged(object? sender, EventArgs args)
    {
        _mode.Refresh();
        _position.Refresh();
        Reload(Selected?.Id);
    }

    private PersonalRunePreset? Selected => _list.SelectedIndex is int index && index >= 0 && index < _presets.Count
        ? _presets[index] : null;

    private void Reload(Guid? selectId = null)
    {
        _presets = _settings.Load().PersonalRunePresets
            .OrderBy(preset => preset.ChampionId).ThenByDescending(preset => preset.Priority).ToList();
        _list.DataSource = _presets.Select(preset =>
        {
            string champion = _champions.GetDisplayName(preset.ChampionId);
            if (string.IsNullOrWhiteSpace(champion)) champion = $"#{preset.ChampionId}";
            return new PresetRow($"{champion} · {preset.Name} · {ModeLabel(preset.Mode)}/{PositionLabel(preset.Position)}" +
                (preset.AutoApply ? (UiLanguage.IsEnglish ? " · Auto" : " · 自动") : ""));
        }).ToArray();
        int index = selectId.HasValue ? _presets.FindIndex(preset => preset.Id == selectId.Value) : -1;
        if (_presets.Count > 0) _list.SelectedIndex = index >= 0 ? index : 0;
    }

    private void ShowSelected()
    {
        var preset = Selected;
        if (preset == null) return;
        _name.Text = preset.Name;
        _mode.SelectedValue = Modes.FirstOrDefault(item => item.Key == preset.Mode);
        _position.SelectedValue = Positions.FirstOrDefault(item => item.Key == preset.Position);
        _priority.Value = Math.Clamp(preset.Priority, -100, 100);
        _auto.Checked = preset.AutoApply;
        _detail.Text = UiLanguage.IsEnglish
            ? $"Champion ID: {preset.ChampionId}\nPrimary/secondary: {preset.PrimaryStyleId}/{preset.SubStyleId}\nRunes: {string.Join(", ", preset.RunePerkIds)}\nSummoner spells: {string.Join(", ", preset.SummonerSpellIds)}"
            : $"英雄 ID：{preset.ChampionId}\n主系/副系：{preset.PrimaryStyleId}/{preset.SubStyleId}\n符文：{string.Join(", ", preset.RunePerkIds)}\n召唤师技能：{string.Join(", ", preset.SummonerSpellIds)}";
    }

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

    private void SaveSelected()
    {
        var preset = Selected;
        if (preset == null) return;
        string name = _name.Text.Trim();
        if (name.Length is < 1 or > 80) { _status.Text = "方案名称需为 1 至 80 个字符。"; return; }
        var settings = _settings.Load();
        var target = settings.PersonalRunePresets.FirstOrDefault(item => item.Id == preset.Id);
        if (target == null) return;
        target.Name = name;
        target.Mode = (_mode.SelectedValue as Choice)?.Key ?? target.Mode;
        target.Position = (_position.SelectedValue as Choice)?.Key ?? target.Position;
        target.Priority = (int)_priority.Value;
        target.AutoApply = _auto.Checked;
        _settings.Save(settings);
        Reload(target.Id);
        _status.Text = "方案已保存。";
    }

    private async Task ApplySelectedAsync()
    {
        var preset = Selected;
        if (preset == null) return;
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
    }

    private static string ModeLabel(string mode) => mode switch
    {
        "ranked" => UiLanguage.IsEnglish ? "Summoner's Rift" : "召唤师峡谷",
        "aram" => UiLanguage.IsEnglish ? "ARAM" : "极地大乱斗",
        "arena" => UiLanguage.IsEnglish ? "Arena" : "斗魂竞技场",
        "urf" => UiLanguage.IsEnglish ? "URF" : "无限火力",
        "nexus_blitz" => UiLanguage.IsEnglish ? "Nexus Blitz" : "极限闪击",
        _ => mode
    };

    private static string PositionLabel(string position) => position switch
    {
        "top" => UiLanguage.IsEnglish ? "Top" : "上路",
        "jungle" => UiLanguage.IsEnglish ? "Jungle" : "打野",
        "mid" => UiLanguage.IsEnglish ? "Mid" : "中路",
        "adc" => UiLanguage.IsEnglish ? "Bottom" : "下路",
        "support" => UiLanguage.IsEnglish ? "Support" : "辅助",
        _ => UiLanguage.IsEnglish ? "Any" : "不限"
    };

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
