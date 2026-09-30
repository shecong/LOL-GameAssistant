using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;

namespace LOL_GameAssistant.Helper;

/// <summary>Runtime Chinese/English localization of existing and newly added WinForms controls.</summary>
internal static class UiLanguage
{
    private static readonly IReadOnlyDictionary<string, string> English = LoadEnglish();
    private static readonly (Regex Pattern, string[] Tokens, string Translation)[] Dynamic = English
        .Where(entry => entry.Key.Contains('{') && entry.Key.Any(IsChinese))
        .OrderByDescending(entry => entry.Key.Length)
        .Select(entry => BuildPattern(entry.Key, entry.Value)).ToArray();
    private static readonly Dictionary<Control, string> Originals = new();
    private static readonly Dictionary<ComboBox, string[]> ComboOriginals = new();
    private sealed class TextState(string source, string applied)
    {
        public string Source = source;
        public string Applied = applied;
    }
    private static readonly ConditionalWeakTable<Control, Dictionary<string, TextState>> PropertyOriginals = new();
    private static readonly ConditionalWeakTable<ListViewItem, TextState[]> ListOriginals = new();
    private static readonly ConditionalWeakTable<ColumnHeader, TextState> ColumnOriginals = new();
    private static readonly string[] LocalizedProperties =
        ["PlaceholderText", "CheckedText", "UnCheckedText", "PrefixText", "SuffixText", "AccessibleName"];
    private static readonly Dictionary<string, string> Overrides = new(StringComparer.Ordinal)
    {
        ["对局"] = "Live Game", ["对局记录"] = "Match History", ["战绩查询"] = "Player Search",
        ["好友"] = "Friends", ["设置"] = "Settings", ["智能建议"] = "Recommendations",
        ["运行诊断"] = "Diagnostics", ["客户端工具"] = "Client Tools",
        ["对局自动化"] = "Match Automation", ["客户端与数据"] = "Client & Data",
        ["外观与窗口"] = "Appearance & Window", ["AI 与推荐"] = "AI & Recommendations",
        ["选人时自动使用"] = "Auto apply during champion select",
        ["海克斯大乱斗 · 增幅侧边栏"] = "ARAM Mayhem · Augment Sidebar",
        ["海克斯增幅推荐"] = "Mayhem Augment Recommendations",
        ["开黑"] = "Premade", ["英雄选择阶段"] = "Champion Select",
        ["位于大厅"] = "In Lobby", ["正在匹配中"] = "Matchmaking",
        ["游戏对局进行中"] = "In Game", ["客户端未启动"] = "Client Not Running",
        ["界面语言："] = "Interface language:", ["中文"] = "Chinese", ["English"] = "English",
        ["首页"] = "Home", ["喊话"] = "Quick Chat", ["开"] = "On", ["关"] = "Off",
        ["浅色"] = "Light", ["深色"] = "Dark", ["跟随系统"] = "Follow System",
        ["蓝方"] = "Blue Team", ["红方"] = "Red Team", ["单双排"] = "Solo/Duo",
        ["灵活组排"] = "Flex Queue", ["负场 -"] = "Losses -", ["胜场 -"] = "Wins -",
        ["近期战绩"] = "Recent Matches", ["排位信息"] = "Ranked Stats",
        ["玩家信息"] = "Player Profile", ["数据状态与当前局势"] = "Data & Current Game",
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
    private static bool _english;
    private static bool _updating;
    private static bool _started;
    private static System.Windows.Forms.Timer? _refreshTimer;
    public static event EventHandler? Changed;

    public static bool IsEnglish => _english;
    public static void Refresh() => ApplyOpenForms(true);

    public static void Start()
    {
        if (_started) return;
        _started = true;
        System.Windows.Forms.Application.Idle += (_, _) => ApplyOpenForms(false);
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();
        System.Windows.Forms.Application.ApplicationExit += (_, _) => _refreshTimer?.Dispose();
    }

    public static void SetMode(string? mode)
    {
        bool english = string.Equals(mode, "en-US", StringComparison.OrdinalIgnoreCase);
        if (_english == english) return;
        _english = english;
        ApplyOpenForms(true);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static string T(string? source)
    {
        if (!_english || string.IsNullOrEmpty(source)) return source ?? "";
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

    private static string TranslateComposite(string source)
    {
        string exact = T(source);
        if (!_english || exact != source) return exact;
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

    private static void ApplyOpenForms(bool refresh)
    {
        if (_updating) return;
        foreach (Form form in System.Windows.Forms.Application.OpenForms.Cast<Form>().ToArray())
            if (!form.IsDisposed && (refresh || !Originals.ContainsKey(form))) Attach(form, refresh);
    }

    private static void Attach(Control control, bool refresh = false)
    {
        if (control.IsDisposed) return;
        bool newlyAttached = !Originals.ContainsKey(control);
        if (newlyAttached)
        {
            Originals[control] = RecoverOriginal(control.Text);
            control.TextChanged += ControlTextChanged;
            control.ControlAdded += (_, args) => { if (args.Control is { } added) Attach(added); };
            control.Disposed += (_, _) =>
            {
                Originals.Remove(control);
                if (control is ComboBox combo) ComboOriginals.Remove(combo);
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

    private static void ControlTextChanged(object? sender, EventArgs args)
    {
        if (_updating || sender is not Control control || control.IsDisposed) return;
        Originals[control] = RecoverOriginal(control.Text);
        ApplyText(control);
    }

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

    private static void ApplyCombo(ComboBox box)
    {
        if (!ComboOriginals.TryGetValue(box, out string[]? original))
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

    private static void ApplyProperties(Control control)
    {
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

    private static bool IsChinese(char c) => c is >= '\u3400' and <= '\u9fff';
    private static string RecoverOriginal(string text) =>
        _english && Reverse.TryGetValue(text, out string? original) ? original : text;

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
