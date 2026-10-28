using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace LOL_GameAssistant.Helper;

/// <summary>Runtime Simplified Chinese, Traditional Chinese and English localization of WinForms controls.</summary>
internal static class UiLanguage
{
    private static readonly IReadOnlyDictionary<string, string> English = LoadEnglish();
    private static readonly (Regex Pattern, string[] Tokens, string Translation)[] Dynamic = English
        .Where(entry => entry.Key.Contains('{') && entry.Key.Any(IsChinese))
        .OrderByDescending(entry => entry.Key.Length)
        .Select(entry => BuildPattern(entry.Key, entry.Value)).ToArray();
    private static readonly ConcurrentDictionary<Control, string> Originals = new();
    private static readonly ConcurrentDictionary<ComboBox, string[]> ComboOriginals = new();
    /// <summary>保存控件原始文本和翻译状态，支持语言往返切换。</summary>
    private sealed class TextState(string source, string applied)
    {
        public string Source = source;
        public string Applied = applied;
    }
    private static readonly ConditionalWeakTable<Control, Dictionary<string, TextState>> PropertyOriginals = new();
    private static readonly ConditionalWeakTable<ListViewItem, TextState[]> ListOriginals = new();
    private static readonly ConditionalWeakTable<ColumnHeader, TextState> ColumnOriginals = new();
    private static readonly ConditionalWeakTable<AntdUI.SegmentedItem, TextState> SegmentOriginals = new();
    private static readonly string[] LocalizedProperties =
        ["PlaceholderText", "CheckedText", "UnCheckedText", "PrefixText", "SuffixText", "AccessibleName"];
    private static readonly Dictionary<string, string> Overrides = new(StringComparer.Ordinal)
    {
        ["对局"] = "Live Game", ["对局记录"] = "Match History", ["战绩查询"] = "Player Search",
        ["回到顶部"] = "Back to top", ["尝试观战"] = "Spectate", ["全部英雄"] = "All champions",
        ["全部结果"] = "All results", ["重置"] = "Reset", ["重开"] = "Remake",
        ["结果"] = "Result", ["英雄"] = "Champion", ["时长"] = "Duration", ["时间"] = "Date",
        ["☆ 收藏"] = "☆ Favorite", ["导出战绩"] = "Export matches",
        ["SGP · 摘要优先 / 详情按需"] = "SGP · Details on demand",
        ["好友"] = "Friends", ["设置"] = "Settings", ["智能建议"] = "Recommendations",
        ["运行诊断"] = "Diagnostics", ["客户端工具"] = "Client Tools",
        ["对局自动化"] = "Match Automation", ["客户端与数据"] = "Client & Data",
        ["外观与窗口"] = "Appearance & Window", ["AI 与推荐"] = "AI & Recommendations",
        ["预选或选择英雄时自动替换符文和装备"] = "Automatically update runes and items when hovering or picking a champion",
        ["符文装备自动配置："] = "Automatic runes and items:",
        ["自动配置默认开启：预选或选择英雄后应用对应符文和装备，切换英雄后自动更新。优先复用助手符文页，页面已满时替换当前可编辑页。点击“一键选择符文装备”可手动调整方案；关闭自动配置后仅后台加载推荐。"] = "Automatic setup is enabled by default. Hovering, picking or changing a champion applies their runes and items. Reuse an assistant rune page first; if pages are full, replace the current editable page. Click Runes & Items to adjust the build manually. Turning automatic setup off only preloads recommendations.",
        ["预选、选择或更换英雄时，按英雄、模式和分路自动应用个人方案或 OP.GG 推荐；个人方案未保存装备时补充推荐出装。同一英雄不重复写入，手动调整方案后保留选择。"] = "Apply a personal or OP.GG build for the champion, mode and role when hovering, picking or changing champions. Add recommended items when a personal preset has no items. Avoid repeated writes for the same champion and preserve manual choices.",
        ["选人阶段检测到已选英雄后后台加载 OP.GG 推荐；点击“一键配置”查看方案，不自动弹窗。"] = "Load OP.GG recommendations in the background after selecting a champion. Click Quick Setup to view builds; no automatic popup.",
        ["推荐方案已就绪，点击一键配置查看并应用。"] = "Recommendations are ready. Click Quick Setup to review and apply a build.",
        ["推荐后台加载失败，可点击一键配置重试。"] = "Background loading failed. Click Quick Setup to retry.",
        ["技能冷却计时 · 功能预览"] = "Skill Cooldown Timers · Preview",
        ["敌我召唤师技能冷却计时"] = "Ally and enemy summoner spell cooldown timers",
        ["我方英雄技能冷却计时"] = "Allied champion ability cooldown timers",
        ["敌方英雄技能冷却计时"] = "Enemy champion ability cooldown timers",
        ["召唤师技能："] = "Summoner spells:", ["我方英雄："] = "Allied champions:",
        ["敌方英雄："] = "Enemy champions:", ["功能状态："] = "Feature status:",
        ["该功能正在测试，即将上线"] = "This feature is being tested and is coming soon",
        ["该功能正在测试，即将上线。当前仅供预览，暂不支持启用。"] = "This feature is being tested and is coming soon. Preview only; it cannot be enabled yet.",
        ["选人时自动使用"] = "Auto apply during champion select",
        ["海克斯大乱斗 · 增幅侧边栏"] = "ARAM Mayhem · Augment Sidebar",
        ["海克斯增幅推荐"] = "Mayhem Augment Recommendations",
        ["开黑"] = "Premade", ["英雄选择阶段"] = "Champion Select",
        ["位于大厅"] = "In Lobby", ["正在匹配中"] = "Matchmaking",
        ["游戏对局进行中"] = "In Game", ["客户端未启动"] = "Client Not Running",
        ["界面语言："] = "Interface language:", ["中文"] = "Chinese", ["English"] = "English",
        ["保存后立即切换简体中文、繁体中文或英文界面。"] = "Switch immediately between Simplified Chinese, Traditional Chinese and English.",
        ["首页"] = "Home", ["喊话"] = "Quick Chat", ["开"] = "On", ["关"] = "Off",
        ["浅色"] = "Light", ["深色"] = "Dark", ["跟随系统"] = "Follow System",
        ["蓝方"] = "Blue Team", ["红方"] = "Red Team", ["单双排"] = "Solo/Duo",
        ["灵活组排"] = "Flex Queue", ["负场 -"] = "Losses -", ["胜场 -"] = "Wins -",
        ["近期战绩"] = "Recent Matches", ["排位信息"] = "Ranked Stats",
        ["玩家信息"] = "Player Profile", ["数据状态与当前局势"] = "Data & Current Game",
        ["返回当前玩家"] = "My Profile", ["我的战绩"] = "My Matches", ["查询玩家"] = "Search Player",
        ["单双排 未排位"] = "Solo/Duo · Unranked", ["灵活组排 未排位"] = "Flex · Unranked",
        ["窗口行为"] = "Window Behavior", ["置顶说明："] = "Pinning note:",
        ["英雄选择"] = "Champion Select", ["匹配与对局"] = "Matchmaking & Games",
        ["我的符文方案"] = "My Rune Presets", ["AI 时间线建议"] = "AI Timeline Advice",
        ["自动匹配："] = "Auto queue:", ["自动接受："] = "Auto accept:",
        ["对局自动刷新："] = "Auto refresh match:", ["自动禁英雄："] = "Auto ban champions:",
        ["自动选英雄："] = "Auto pick champion:", ["禁用英雄列表："] = "Ban priority:",
        ["选用英雄列表："] = "Pick priority:"
    };
    private static readonly IReadOnlyDictionary<string, string> Reverse = English
        .Concat(Overrides)
        .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
        .GroupBy(entry => entry.Value, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.Ordinal);
    private static string _mode = "zh-CN";
    [ThreadStatic] private static bool _updating;
    private static bool _started;
    // Invalidated/Disposed can arrive from different UI threads. Keep the owning
    // thread with each entry so an idle loop never updates another thread's control.
    private static readonly ConcurrentDictionary<Control, int> Dirty = new();
    public static event EventHandler? Changed;

    /// <summary>判断当前界面语言是否为英文。</summary>
    public static bool IsEnglish => _mode == "en-US";
    public static bool IsTraditionalChinese => _mode == "zh-TW";
    public static string Mode => _mode;
    /// <summary>刷新当前对象展示的状态或数据。</summary>
    public static void Refresh() => ApplyOpenForms(true);

    /// <summary>启动当前服务或组件的运行流程。</summary>
    public static void Start()
    {
        if (_started) return;
        _started = true;
        System.Windows.Forms.Application.Idle += ProcessIdle;
        System.Windows.Forms.Application.ApplicationExit += Stop;
    }

    private static void ProcessIdle(object? sender, EventArgs args)
    {
        ApplyOpenForms(false);
        foreach (var (control, threadId) in Dirty.ToArray())
        {
            if (threadId != Environment.CurrentManagedThreadId) continue;
            if (!Dirty.TryRemove(control, out _) || control.IsDisposed || control.Disposing) continue;
            ApplyProperties(control);
            if (control is ComboBox box) ApplyCombo(box);
            if (control is ListView list) ApplyList(list);
        }
    }

    private static void Stop(object? sender, EventArgs args)
    {
        System.Windows.Forms.Application.Idle -= ProcessIdle;
        System.Windows.Forms.Application.ApplicationExit -= Stop;
        Dirty.Clear();
        _started = false;
    }

    /// <summary>切换当前显示模式并通知相关订阅者。</summary>
    public static void SetMode(string? mode)
    {
        string normalized = string.Equals(mode, "en-US", StringComparison.OrdinalIgnoreCase) ? "en-US"
            : string.Equals(mode, "zh-TW", StringComparison.OrdinalIgnoreCase) ? "zh-TW" : "zh-CN";
        if (_mode == normalized) return;
        _mode = normalized;
        ApplyOpenForms(true);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>将原始界面文本转换为当前语言的文本。</summary>
    public static string T(string? source)
    {
        if (string.IsNullOrEmpty(source)) return source ?? "";
        if (IsTraditionalChinese)
            return ConvertChinese(source, traditional: true);
        if (!IsEnglish) return source;
        if (Overrides.TryGetValue(source, out string? corrected)) return corrected;
        if (English.TryGetValue(source, out string? exact)) return exact;
        if (!source.Any(IsChinese)) return source;
        foreach (var (pattern, tokens, translated) in Dynamic)
        {
            Match match = pattern.Match(source);
            if (!match.Success) continue;
            string rendered = translated;
            for (int index = 0; index < tokens.Length && index + 1 < match.Groups.Count; index++)
                rendered = rendered.Replace(tokens[index], match.Groups[index + 1].Value,
                    StringComparison.Ordinal);
            return rendered;
        }
        return source;
    }

    /// <summary>翻译包含动态片段的组合文本。</summary>
    private static string TranslateComposite(string source)
    {
        string exact = T(source);
        if (!IsEnglish || exact != source) return exact;
        string[] lines = source.Replace("\r\n", "\n").Split('\n');
        string[] prefixes = ["状态：", "说明：", "阶段：", "模式：", "英雄：", "游戏时间：",
            "当前金币：", "已购装备：", "可见敌方阵容：", "依据：", "来源："];
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            string? prefix = prefixes.FirstOrDefault(item => line.StartsWith(item, StringComparison.Ordinal));
            lines[index] = prefix == null ? T(line) : T(prefix) + T(line[prefix.Length..]);
        }
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>将当前语言设置应用到已打开的窗口。</summary>
    private static void ApplyOpenForms(bool refresh)
    {
        if (_updating) return;
        foreach (Form form in System.Windows.Forms.Application.OpenForms.Cast<Form>().ToArray())
        {
            if (form.IsDisposed || form.Disposing) continue;
            if (form.InvokeRequired)
            {
                if (refresh)
                {
                    try { form.BeginInvoke(() => Attach(form, true)); }
                    catch (InvalidOperationException) when (form.IsDisposed || !form.IsHandleCreated) { }
                }
                continue;
            }
            if (refresh || !Originals.ContainsKey(form)) Attach(form, refresh);
        }
    }

    /// <summary>绑定目标控件或窗口的相关事件。</summary>
    private static void Attach(Control control, bool refresh = false)
    {
        if (control.IsDisposed || control.Disposing) return;
        bool newlyAttached = !Originals.ContainsKey(control);
        if (newlyAttached)
        {
            Originals[control] = RecoverOriginal(control.Text);
            control.TextChanged += ControlTextChanged;
            if (control is ComboBox or ListView || LocalizedProperties.Any(name => control.GetType().GetProperty(name) != null))
            {
                int threadId = Environment.CurrentManagedThreadId;
                control.Invalidated += (_, _) =>
                {
                    if (!_updating && !control.IsDisposed && !control.Disposing) Dirty[control] = threadId;
                };
            }
            control.ControlAdded += (_, args) => { if (args.Control is { } added) Attach(added); };
            control.Disposed += (_, _) =>
            {
                Originals.TryRemove(control, out _);
                Dirty.TryRemove(control, out _);
                if (control is ComboBox combo) ComboOriginals.TryRemove(combo, out _);
            };
        }
        if (newlyAttached || refresh)
        {
            ApplyText(control);
            ApplyProperties(control);
            if (control is ComboBox box) ApplyCombo(box);
            if (control is ListView list) ApplyList(list);
        }
        foreach (Control child in control.Controls) Attach(child, refresh);
    }

    /// <summary>控件文本变化后同步其本地化状态。</summary>
    private static void ControlTextChanged(object? sender, EventArgs args)
    {
        if (_updating || sender is not Control control || control.IsDisposed) return;
        Originals[control] = RecoverOriginal(control.Text);
        ApplyText(control);
    }

    /// <summary>为控件设置当前语言对应的显示文本。</summary>
    private static void ApplyText(Control control)
    {
        if (control is TextBoxBase textBox && !textBox.ReadOnly) return;
        if (control is ComboBox or AntdUI.Input) return;
        if (!Originals.TryGetValue(control, out string? original)) return;
        string desired = control is RichTextBox ? TranslateComposite(original) : T(original);
        if (control.Text == desired) return;
        _updating = true;
        try { control.Text = desired; }
        finally { _updating = false; }
    }

    /// <summary>翻译下拉选择控件中的条目。</summary>
    private static void ApplyCombo(ComboBox box)
    {
        if (!ComboOriginals.TryGetValue(box, out string[]? original) || original.Length != box.Items.Count)
        {
            original = box.Items.Cast<object>().Select(item => RecoverOriginal(item.ToString() ?? "")).ToArray();
            ComboOriginals[box] = original;
        }
        if (box.Items.Count != original.Length) return;
        int selected = box.SelectedIndex;
        _updating = true;
        try
        {
            for (int index = 0; index < original.Length; index++)
                if (box.Items[index] is string && !string.Equals(box.Items[index]?.ToString(), T(original[index]), StringComparison.Ordinal))
                    box.Items[index] = T(original[index]);
            if (selected >= 0 && box.SelectedIndex != selected) box.SelectedIndex = selected;
        }
        finally { _updating = false; }
    }

    /// <summary>翻译控件中除主文本外的相关文字属性。</summary>
    private static void ApplyProperties(Control control)
    {
        if (control is AntdUI.Segmented segmented)
        {
            _updating = true;
            try
            {
                foreach (AntdUI.SegmentedItem item in segmented.Items)
                {
                    var state = SegmentOriginals.GetValue(item, value => new TextState(RecoverOriginal(value.Text ?? ""), value.Text ?? ""));
                    if (item.Text != state.Applied) state.Source = RecoverOriginal(item.Text ?? "");
                    item.Text = state.Applied = T(state.Source);
                }
            }
            finally { _updating = false; }
        }
        Dictionary<string, TextState> originals = PropertyOriginals.GetOrCreateValue(control);
        foreach (string name in LocalizedProperties)
        {
            var property = control.GetType().GetProperty(name);
            if (property is not { CanRead: true, CanWrite: true, PropertyType: { } propertyType } ||
                propertyType != typeof(string)) continue;
            string? current = property.GetValue(control) as string;
            if (!originals.TryGetValue(name, out TextState? state))
                originals[name] = state = new TextState(RecoverOriginal(current ?? ""), current ?? "");
            else if (current != state.Applied)
                state.Source = RecoverOriginal(current ?? "");
            string desired = T(state.Source);
            if (current == desired) continue;
            try { property.SetValue(control, desired); state.Applied = desired; }
            catch { /* Some third-party controls reject assignments during handle creation. */ }
        }
    }

    /// <summary>翻译列表控件中的显示条目。</summary>
    private static void ApplyList(ListView list)
    {
        foreach (ListViewItem item in list.Items)
        {
            TextState[] states = ListOriginals.GetValue(item,
                value => value.SubItems.Cast<ListViewItem.ListViewSubItem>()
                    .Select(part => new TextState(RecoverOriginal(part.Text), part.Text)).ToArray());
            for (int index = 0; index < states.Length && index < item.SubItems.Count; index++)
            {
                TextState state = states[index];
                if (item.SubItems[index].Text != state.Applied) state.Source = RecoverOriginal(item.SubItems[index].Text);
                item.SubItems[index].Text = state.Applied = T(state.Source);
            }
        }
        foreach (ColumnHeader column in list.Columns)
        {
            TextState state = ColumnOriginals.GetValue(column,
                value => new TextState(RecoverOriginal(value.Text), value.Text));
            if (column.Text != state.Applied) state.Source = RecoverOriginal(column.Text);
            column.Text = state.Applied = T(state.Source);
        }
    }

    /// <summary>检查文本中是否包含中文字符。</summary>
    private static bool IsChinese(char c) => c is >= '\u3400' and <= '\u9fff';
    /// <summary>从当前文本和已保存状态中恢复原始文案。</summary>
    private static string RecoverOriginal(string? text)
    {
        // Some native/third-party controls return null before their text is initialized.
        // Normalize here so attaching or refreshing any page never uses a null dictionary key.
        if (string.IsNullOrEmpty(text)) return "";
        if (Reverse.TryGetValue(text, out string? original)) return original;
        return IsTraditionalChinese ? ConvertChinese(text, traditional: false) : text;
    }

    // Unicode mapping works without installing a Chinese code page or language pack.
    private static string ConvertChinese(string source, bool traditional)
    {
        if (!source.Any(IsChinese)) return source;
        const uint simplifiedChinese = 0x02000000, traditionalChinese = 0x04000000;
        uint flags = traditional ? traditionalChinese : simplifiedChinese;
        int length = LCMapStringEx("zh-CN", flags, source, source.Length, null, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (length == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var result = new char[length];
        int written = LCMapStringEx("zh-CN", flags, source, source.Length, result, result.Length, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (written == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return new string(result, 0, written);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern int LCMapStringEx(string localeName, uint flags, string source, int sourceLength,
        [Out] char[]? destination, int destinationLength, IntPtr versionInformation, IntPtr reserved, IntPtr sortHandle);

    /// <summary>为包含动态片段的文本构建匹配模式。</summary>
    private static (Regex Pattern, string[] Tokens, string Translation) BuildPattern(string source, string translation)
    {
        MatchCollection matches = Regex.Matches(source, @"\{[^{}]+\}");
        var pattern = new System.Text.StringBuilder("^");
        int cursor = 0;
        foreach (Match match in matches)
        {
            pattern.Append(Regex.Escape(source[cursor..match.Index])).Append("(.+?)");
            cursor = match.Index + match.Length;
        }
        pattern.Append(Regex.Escape(source[cursor..])).Append('$');
        return (new Regex(pattern.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant),
            matches.Select(match => match.Value).ToArray(), translation);
    }

    /// <summary>读取内置英文翻译表。</summary>
    private static IReadOnlyDictionary<string, string> LoadEnglish()
    {
        try
        {
            string file = Path.Combine(AppContext.BaseDirectory, "Resources", "locale-en.json");
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))
                ?? new Dictionary<string, string>();
        }
        catch { return new Dictionary<string, string>(); }
    }
}
