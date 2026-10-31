using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.MatchAnalysis;
using LOL_GameAssistant.Helper;
using System.Globalization;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>当前查询玩家的英雄池与复盘视图，两个页签共用同一份有来源的样本。</summary>
internal sealed partial class PlayerGrowthControl : UserControl, IThemeAware
{
    private readonly IPlayerGrowthService _service;
    private readonly IMatchHistoryService _matches;
    private readonly IChampionCatalog _champions;
    private readonly UiRequestScope _requests = new();
    private readonly AntdUI.Table _table = new() { Dock = DockStyle.Fill, Bordered = true };
    private readonly AntdUI.Label _scope = new() { Dock = DockStyle.Top, Height = 58, Padding = new Padding(12, 5, 12, 5) };
    private readonly AntdUI.Label _summary = new() { Dock = DockStyle.Top, Height = 80, Padding = new Padding(12), Font = new Font(UiMetrics.FontFamily, 11, FontStyle.Bold) };
    private readonly AntdUI.Select _mode = new() { Width = 180, Height = 36, ReadOnly = true };
    private readonly AntdUI.Input _search = new() { Width = 175, Height = 36, PlaceholderText = "搜索英雄" };
    private readonly AntdUI.Checkbox _training = new() { Text = "仅主练", Width = 90, Height = 36 };
    private readonly AntdUI.Checkbox _owned = new() { Text = "仅已拥有", Width = 105, Height = 36 };
    private readonly AntdUI.Input _note = new() { Width = 250, Height = 36, PlaceholderText = "复盘笔记（清空可取消标记）" };
    private readonly AntdUI.Checkbox _onlyNotes = new() { Text = "仅标记", Width = 90, Height = 36 };
    private readonly AntdUI.Button _saveNote = new() { Text = "保存复盘标记", AutoSize = true };
    private readonly AntdUI.Button _mark = new() { Text = "切换主练标记", AutoSize = true };
    private readonly AntdUI.Button _detail = new() { Text = "查看对局详情", AutoSize = true };
    private readonly AntdUI.Button _replay = new() { Text = "回放状态 / 下载", AutoSize = true };
    private readonly FlowLayoutPanel _filters = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(8) };
    private readonly AntdUI.Input _analysis = new() { Name = "growthAnalysis", Dock = DockStyle.Bottom, Height = 230, ReadOnly = true, Multiline = true };
    private readonly AntdUI.Select _focus = new() { Name = "growthFocus", Width = 180, Height = 36, ReadOnly = true };
    private PlayerGrowthSnapshot? _snapshot;
    private ChampionArchiveEntry[] _visibleChampions = [];
    private GrowthMatch[] _visibleMatches = [];
    private string? _puuid;
    private bool _pool = true, _binding;
    private string _selectedMode = "全部";
    public event Action<string>? ModeChanged;
    private sealed record PoolRow(string Name, string Training, string Owned, string Mastery, string Games, string WinRate, string Kda, string Cs, string Damage, string Vision, string Status);
    private sealed record ReviewRow(string Date, string Champion, string Mode, string Result, string Kda, string Cs, string Damage, string Vision, string Focus);
    internal PlayerGrowthControl(IPlayerGrowthService service, IMatchHistoryService matches, IChampionCatalog champions,
        ICompanionExperienceStore? experience = null, IPatchWatchService? patches = null)
    {
        _service = service; _matches = matches; _champions = champions; Dock = DockStyle.Fill;
        _summary.TextMultiLine = _scope.TextMultiLine = true;
        _experienceStore = experience ?? AppCompositionRoot.CompanionExperienceStore;
        _patches = patches ?? AppCompositionRoot.PatchWatchService;
        _mode.Items.Add("全部"); _mode.SelectedIndex = 0;
        _mode.SelectedIndexChanged += async (_, _) => { if (!_binding) { _selectedMode = _mode.SelectedValue?.ToString() ?? "全部"; ModeChanged?.Invoke(_selectedMode); await LoadAsync(false); } };
        _search.TextChanged += (_, _) => Render(); _training.CheckedChanged += (_, _) => Render(); _owned.CheckedChanged += (_, _) => Render();
        var refresh = new AntdUI.Button { Text = "刷新样本", AutoSize = true }; refresh.Click += async (_, _) => await LoadAsync(true);
        var preset = new AntdUI.Button { Text = "配置个人方案", AutoSize = true };
        preset.Click += (_, _) =>
        {
            int index = _table.SelectedIndex;
            int? id = _pool && index >= 0 && index < _visibleChampions.Length ? _visibleChampions[index].ChampionId : SelectedMatch()?.ChampionId;
            using var form = new RunePresetManagerForm(AppCompositionRoot.ApplicationSettingsStore, AppCompositionRoot.OpggBuildApplyService, AppCompositionRoot.AiCoachingService, championId: id);
            form.ShowDialog(FindForm());
        };
        _filters.Controls.AddRange([_focus, _mode, _search, _training, _owned, refresh, _mark, _detail, _replay, preset, _note, _onlyNotes, _saveNote]);
        InitializeExperienceActions();
        _onlyNotes.CheckedChanged += (_, _) => Render(); _focus.SelectedIndexChanged += (_, _) => { if (!_binding) Render(); };
        _table.SelectIndexChanged += (_, _) => { RenderSelection(); var match = SelectedMatch(); if (match != null) _note.Text = _snapshot?.ReviewNotes.GetValueOrDefault(match.GameId) ?? ""; };
        _saveNote.Click += async (_, _) => { var match = SelectedMatch(); if (match == null || _puuid == null) return; try { await _service.SetReviewNoteAsync(_puuid, match.GameId, _note.Text ?? "", _requests.Token); await LoadAsync(false); } catch (OperationCanceledException) { } catch (Exception e) { if (!IsDisposed) _scope.Text = e.Message; } };
        var split = new SplitContainer { Name = "growthWorkspace", Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Size = new Size(900, 500), SplitterDistance = 265, Panel1MinSize = 80, Panel2MinSize = 100 };
        _analysis.Dock = DockStyle.Fill; split.Panel1.Controls.Add(_table); split.Panel2.Controls.Add(_analysis);
        Controls.Add(split); Controls.Add(_summary); Controls.Add(_scope); Controls.Add(_filters);
        Controls.SetChildIndex(_goalProgress, Controls.Count - 1);
        _mark.Click += async (_, _) =>
        {
            int i = _table.SelectedIndex; if (!_pool || i < 0 || i >= _visibleChampions.Length || _puuid == null) return;
            try { var c = _visibleChampions[i]; await _service.SetTrainingAsync(_puuid, c.ChampionId, !c.Training, _requests.Token); await LoadAsync(false); }
            catch (OperationCanceledException) { }
            catch (Exception e) { _scope.Text = e.Message; }
        };
        _detail.Click += async (_, _) =>
        {
            var selected = SelectedMatch(); if (selected == null || _puuid == null) return;
            using var request = _requests.Begin(); string puuid = _puuid;
            try { var detail = await _matches.GetDetailAsync(selected.GameId, cancellationToken: request.Token); if (!request.IsCurrent || puuid != _puuid || IsDisposed) return; if (detail == null) { _scope.Text = "该局详情暂不可用"; return; } using var form = new MatchDetailForm(detail, puuid); form.ShowDialog(FindForm()); }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!IsDisposed) _scope.Text = e.Message; }
        };
        _replay.Click += (_, _) => { var selected = SelectedMatch(); if (selected != null) { using var f = new ReplayManagerForm(selected.GameId, AppCompositionRoot.ClientFeatureService); f.ShowDialog(FindForm()); } };
        Disposed += (_, _) => { _patchLoads.Dispose(); _requests.Dispose(); }; UiTheme.Apply(this); ConfigureColumns();
    }
    private GrowthMatch? SelectedMatch() { int i = _table.SelectedIndex; return !_pool && i >= 0 && i < _visibleMatches.Length ? _visibleMatches[i] : null; }
    public async Task ShowAsync(string? puuid, bool pool, string mode, bool allowReplay = true)
    {
        bool changed = _puuid != puuid || _selectedMode != mode;
        if (_puuid != puuid) { _patchLoads.Cancel(); _experience = new(); _patchNotice = _patchCheckedScope = ""; _goalProgress.Text = ""; }
        _pool = pool; _puuid = puuid; _selectedMode = mode;
        _binding = true;
        try { _focus.Items.Clear(); _focus.Items.AddRange(pool ? new object[] { "近期使用", "全部英雄", "主练候选", "重点复盘" } : new object[] { "全部对局", "失败对局", "高死亡对局" }); _focus.SelectedIndex = 0; }
        finally { _binding = false; }
        _summary.Height = 82; _analysis.Text = "选择一行查看分析、个人基线和练习建议";
        _search.Visible = _training.Visible = _owned.Visible = _mark.Visible = pool;
        _detail.Visible = _replay.Visible = _note.Visible = _onlyNotes.Visible = _saveNote.Visible = !pool; ConfigureColumns();
        _replay.Visible = !pool && allowReplay;
        if (changed || _snapshot == null) { _snapshot = null; _table.DataSource = null; await LoadAsync(false); } else Render();
    }
    public void Cancel() { _requests.Cancel(); _patchLoads.Cancel(); }
    public void ResetPlayer() { Cancel(); _puuid = null; _snapshot = null; _experience = new(); _patchNotice = _patchCheckedScope = ""; _goalProgress.Text = ""; _table.DataSource = null; _analysis.Text = ""; _scope.Text = "正在切换查询玩家…"; }
    private async Task LoadAsync(bool refresh)
    {
        if (string.IsNullOrWhiteSpace(_puuid)) { _scope.Text = "请先查询玩家，再查看英雄池档案与个人复盘"; return; }
        using var request = _requests.Begin(); string puuid = _puuid, mode = _selectedMode; _scope.Text = "正在读取有限战绩样本与英雄档案…";
        try
        {
            using var performance = new UiPerformanceSample("成长样本");
            var profileTask = LoadExperienceAsync(puuid, request.Token);
            var growthTask = _service.LoadAsync(puuid, mode, request.Token, refresh);
            await Task.WhenAll(profileTask, growthTask);
            var result = await growthTask;
            var experience = await profileTask;
            if (!request.IsCurrent || IsDisposed || puuid != _puuid || mode != _selectedMode) return;
            _snapshot = result; _binding = true;
            _experience = experience;
            try { _mode.Items.Clear(); _mode.Items.Add("全部"); foreach (string value in result.Modes.Where(x => x != "全部")) _mode.Items.Add(value); if (!_mode.Items.Contains(mode)) _mode.Items.Add(mode); _mode.SelectedValue = mode; }
            finally { _binding = false; }
            Render();
            _ = CheckPatchNoticeAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (request.IsCurrent && !IsDisposed) _scope.Text = "读取失败：" + e.Message + (_snapshot != null ? "；保留上次样本，请注意采集时间。" : ""); }
    }
    private void ConfigureColumns()
    {
        _table.Columns.Clear();
        var columns = _pool ? new[] { ("Name", "英雄"), ("Training", "主练"), ("Owned", "拥有"), ("Mastery", "熟练度 / 等级"), ("Games", "样本"), ("WinRate", "胜率"), ("Kda", "KDA"), ("Cs", "补刀/分"), ("Damage", "伤害/分"), ("Vision", "视野/分"), ("Status", "近期评估") }
            : new[] { ("Date", "时间"), ("Champion", "英雄"), ("Mode", "模式"), ("Result", "胜负"), ("Kda", "KDA"), ("Cs", "补刀/分"), ("Damage", "伤害/分"), ("Vision", "视野/分"), ("Focus", "复盘标记 / 死亡") };
        foreach (var (key, title) in columns) _table.Columns.Add(new AntdUI.Column(key, UiLanguage.T(title))
            { Width = key == "Status" ? "190" : key is "Name" or "Date" or "Mastery" or "Mode" ? "140" : "85", LineBreak = true });
    }
    private static string Number(double? value) => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—";
    private static double? Rate(IEnumerable<GrowthMatch> matches) { var a = matches.Where(m => m.Won.HasValue).ToArray(); return a.Length == 0 ? null : a.Count(m => m.Won == true) * 100d / a.Length; }
    private void Render()
    {
        if (_snapshot == null || IsDisposed) return;
        RenderExperience();
        var report = _snapshot.Report;
        _scope.Text = $"{report.Scope}\n{(_snapshot.Cached ? "缓存" : "采集")} {_snapshot.AcquiredAt.ToLocalTime():MM-dd HH:mm} · {_snapshot.Message}";
        _summary.Text = $"胜率 {Number(report.WinRate)}%（有效胜负 {report.ResultSamples} 场） · 平均 KDA {Number(PlayerGrowthCalculator.Average(report.Matches.Select(x => x.Kda)))} · 使用英雄 {report.Champions.Count} 位\n伤害/分 {Number(PlayerGrowthCalculator.Average(report.Matches.Select(m => m.DamagePerMinute)))} · 视野/分 {Number(PlayerGrowthCalculator.Average(report.Matches.Select(m => m.VisionPerMinute)))} · 点击下方记录查看完整分析";
        if (!_pool)
        {
            var recent = report.Matches.Take(10).ToArray(); var earlier = report.Matches.Skip(10).Take(10).ToArray();
            _summary.Text += _selectedMode == "全部" ? "\n选择单一模式可比较最近 10 场与此前 10 场的个人趋势；不混合不同玩法比较。" :
                $"\n最近 {recent.Length} 场 / 此前 {earlier.Length} 场 · 胜率 {Number(Rate(recent))}% / {Number(Rate(earlier))}% · 补刀/分 {Number(PlayerGrowthCalculator.Average(recent.Select(m => m.CsPerMinute)))} / {Number(PlayerGrowthCalculator.Average(earlier.Select(m => m.CsPerMinute)))}";
        }
        if (_pool)
        {
            _visibleChampions = _snapshot.Champions.Where(c => (_focus.SelectedIndex != 0 || c.Recent != null || c.Training) && (_focus.SelectedIndex != 2 || c.Recent is { ResultSamples: >= 5, WinRate: >= 55 }) && (_focus.SelectedIndex != 3 || c.Recent is { ResultSamples: >= 5, WinRate: < 45 }) && (!_training.Checked || c.Training) && (!_owned.Checked || c.Owned == true) &&
                (string.IsNullOrWhiteSpace(_search.Text) || c.Name.Contains(_search.Text.Trim(), StringComparison.OrdinalIgnoreCase) || c.ChampionId.ToString() == _search.Text.Trim())).ToArray();
            _table.DataSource = _visibleChampions.Select(c => new PoolRow(c.Name, c.Training ? "★" : "—", c.Owned is true ? "已拥有" : c.Owned is false ? "未拥有" : "未知",
                c.MasteryPoints.HasValue ? $"{c.MasteryPoints:N0} / {c.MasteryLevel?.ToString() ?? "—"}" : "—", c.Recent?.Games.ToString() ?? "0", Number(c.Recent?.WinRate), Number(c.Recent?.Kda), Number(c.Recent?.CsPerMinute), Number(c.Recent?.DamagePerMinute), Number(c.Recent?.VisionPerMinute), GrowthInsights.ChampionStatus(c.Recent))).ToArray();
        }
        else
        {
            _visibleMatches = report.Matches.Where(m => (_focus.SelectedIndex != 1 || m.Won == false) && (_focus.SelectedIndex != 2 || m.Deaths >= 8) && !_onlyNotes.Checked || _snapshot.ReviewNotes.ContainsKey(m.GameId)).ToArray();
            _table.DataSource = _visibleMatches.Select(m => new ReviewRow(m.CreatedAt > 0 && m.CreatedAt < 253402300799000 ? DateTimeOffset.FromUnixTimeMilliseconds(m.CreatedAt).ToLocalTime().ToString("MM-dd HH:mm") : "—",
                _champions.GetDisplayName(m.ChampionId) is { Length: > 0 } n ? n : $"#{m.ChampionId}", m.Mode, m.Won == true ? "胜利" : m.Won == false ? "失败" : "未知",
                Number(m.Kda), Number(m.CsPerMinute), Number(m.DamagePerMinute), Number(m.VisionPerMinute),
                $"{(_snapshot.ReviewNotes.ContainsKey(m.GameId) ? "★ 有笔记 · " : "")}{m.Deaths?.ToString() ?? "—"} 次死亡")).ToArray();
        }
        RenderSelection();
        _note.Text = SelectedMatch() is { } selected ? _snapshot.ReviewNotes.GetValueOrDefault(selected.GameId) ?? "" : "";
    }
    private void RenderSelection()
    {
        if (_snapshot == null) return;
        int index = _table.SelectedIndex;
        if (!_pool)
        {
            var match = SelectedMatch();
            _analysis.Text = match == null ? GrowthInsights.Overview(_snapshot.Report) : GrowthInsights.Review(match, _snapshot.Report.Matches);
            _detail.Enabled = _replay.Enabled = _saveNote.Enabled = match != null;
            return;
        }
        if (index < 0 || index >= _visibleChampions.Length)
        { _analysis.Text = GrowthInsights.Overview(_snapshot.Report); return; }
        var champion = _visibleChampions[index];
        var games = _snapshot.Report.Matches.Where(m => m.ChampionId == champion.ChampionId).ToArray();
        _analysis.Text = $"{champion.Name} · {GrowthInsights.ChampionStatus(champion.Recent)}\r\n" +
            $"{(champion.Training ? "已加入主练" : "可点击切换主练标记，加入个人训练池")} · 熟练度 {champion.MasteryPoints?.ToString("N0") ?? "—"}\r\n" +
            GrowthInsights.Overview(new GrowthReport(games, _snapshot.Report.Champions.Where(c => c.ChampionId == champion.ChampionId).ToArray(), 0, champion.Name));
    }
    public void ApplyTheme(ThemePalette p)
    {
        AntdContentTheme.Apply(this, p); _goalProgress.ForeColor = p.TextSecondary;
        BackColor = p.Surface; _table.BackColor = p.SurfaceRaised; _scope.ForeColor = p.TextSecondary; _summary.ForeColor = p.TextPrimary;
        _table.ColumnBack = p.SurfaceMuted; _table.ColumnFore = p.TextPrimary; _table.BorderColor = p.Border;
        _training.ForeColor = _owned.ForeColor = _onlyNotes.ForeColor = _table.ForeColor = p.TextPrimary;
    }
}
