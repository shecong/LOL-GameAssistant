using System.Diagnostics;
using System.Runtime.InteropServices;
using LOL_GameAssistant.Application.ChampionSelect;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.ChampionSelect;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Ranked;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>选人阶段的只读伴随窗，跟随 LeagueClientUx 窗口移动、缩放和最小化。</summary>
internal sealed class ChampSelectCompanionForm : Form
{
    private readonly IChampionSelectService _selection = AppCompositionRoot.ChampionSelectService;
    private readonly IPlayerProfileService _players = AppCompositionRoot.PlayerProfileService;
    private readonly IRankedStatsService _ranked = AppCompositionRoot.RankedStatsService;
    private readonly IMatchHistoryService _matches = AppCompositionRoot.MatchHistoryService;
    private readonly IApplicationSettingsStore _settings = AppCompositionRoot.ApplicationSettingsStore;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 700 };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly FlowLayoutPanel _cards = new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
        WrapContents = false, AutoScroll = true, Padding = new Padding(9)
    };
    private readonly AntdUI.Label _automation = new() { Dock = DockStyle.Bottom, Height = 52, ForeColor = Color.LightGray };
    private bool _tracking;
    private bool _loading;
    private string _lastRoster = "";
    private DateTimeOffset _lastRefresh;
    private DateTimeOffset _lastRosterDataAt;
    private int _languageRevision;

    public ChampSelectCompanionForm()
    {
        Text = "LOL 选人伴随窗";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(22, 27, 39);
        ForeColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9);
        Size = new Size(350, 600);
        Controls.Add(_cards);
        Controls.Add(_automation);
        Controls.Add(new AntdUI.Label
        {
            Text = "选人队友 · 最近 20 场", Dock = DockStyle.Top, Height = 36,
            ForeColor = Color.White, TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font, FontStyle.Bold)
        });
        _timer.Tick += async (_, _) => await RefreshAsync();
        UiLanguage.Changed += LanguageChanged;
        Disposed += (_, _) =>
        {
            _lifetime.Cancel();
            UiLanguage.Changed -= LanguageChanged;
            _timer.Dispose();
            _lifetime.Dispose();
        };
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            return parameters;
        }
    }

    public void StartTracking()
    {
        if (IsDisposed) return;
        _tracking = true;
        _timer.Start();
        _ = RefreshAsync();
    }

    public void StopTracking()
    {
        if (IsDisposed) return;
        _tracking = false;
        _timer.Stop();
        _lastRoster = "";
        _lastRosterDataAt = DateTimeOffset.MinValue;
        Hide();
    }

    private void LanguageChanged(object? sender, EventArgs args)
    {
        _languageRevision++;
        _lastRoster = "";
        _lastRefresh = DateTimeOffset.MinValue;
        if (_tracking) _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (!_tracking || _loading || IsDisposed) return;
        if (!TryGetClientBounds(out Rectangle client))
        { if (Visible) Hide(); return; }
        FollowClient(client);
        if (!Visible) Show();
        if (DateTimeOffset.UtcNow - _lastRefresh < TimeSpan.FromSeconds(4)) return;
        _lastRefresh = DateTimeOffset.UtcNow;
        _loading = true;
        int languageRevision = _languageRevision;
        try
        {
            ChampionSelectionSnapshot? selection = await _selection.GetSessionAsync(_lifetime.Token);
            if (!_tracking || IsDisposed || _lifetime.IsCancellationRequested) return;
            if (selection == null) return;
            string roster = string.Join("|", selection.MyTeam.Select(member => member.Puuid));
            if (roster == _lastRoster && _cards.Controls.Count > 0 &&
                DateTimeOffset.UtcNow - _lastRosterDataAt < TimeSpan.FromSeconds(30)) return;
            _lastRoster = roster;
            var config = _settings.Load();
            _automation.Text = UiLanguage.IsEnglish
                ? $"Auto ban: {(config.AutoBan ? string.Join(", ", config.BanChampions.Take(2)) : "Off")}\n" +
                  $"Auto pick: {(config.AutoPick ? string.Join(", ", config.PickChampions.Take(2)) : "Off")}"
                : $"自动禁用：{(config.AutoBan ? string.Join("、", config.BanChampions.Take(2)) : "关闭")}\n" +
                  $"自动选用：{(config.AutoPick ? string.Join("、", config.PickChampions.Take(2)) : "关闭")}";
            var allies = selection.MyTeam
                .Where(member => member.CellId != selection.LocalPlayerCellId)
                .Take(4).ToArray();
            var summaries = await Task.WhenAll(allies.Select(LoadAllyAsync));
            if (!_tracking || IsDisposed || roster != _lastRoster || languageRevision != _languageRevision) return;
            _lastRosterDataAt = DateTimeOffset.UtcNow;
            _cards.SuspendLayout();
            try
            {
                _cards.Controls.Clear();
                foreach (string summary in summaries)
                {
                    _cards.Controls.Add(new AntdUI.Label
                    {
                        Text = summary, Width = Math.Max(200, _cards.ClientSize.Width - 20),
                        Height = 100, Padding = new Padding(10), Margin = new Padding(0, 0, 0, 7),
                        BackColor = Color.FromArgb(38, 47, 67), ForeColor = Color.White
                    });
                }
            }
            finally { _cards.ResumeLayout(); }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { if (!IsDisposed) _automation.Text = UiLanguage.T($"队友数据暂不可用：{ex.Message}"); }
        finally { _loading = false; }
    }

    private async Task<string> LoadAllyAsync(ChampionSelectionMember member)
    {
        if (string.IsNullOrWhiteSpace(member.Puuid)) return UiLanguage.IsEnglish
            ? "Anonymous ally\nWaiting for client data" : "未公开身份的队友\n等待客户端提供资料";
        string name = UiLanguage.IsEnglish ? "Ally" : "队友";
        string rank = UiLanguage.IsEnglish ? "Rank unavailable" : "段位未获取";
        string recent = UiLanguage.IsEnglish ? "Recent matches unavailable" : "近期战绩未获取";
        try
        {
            var profile = await _players.GetByPuuidAsync(member.Puuid);
            name = profile?.RiotId ?? name;
        }
        catch { }
        try
        {
            RankedOverview? overview = await _ranked.GetAsync(member.Puuid);
            RankedQueue? solo = overview?.GetQueue(RankedQueues.Solo5x5);
            rank = RankedDisplayFormatter.FormatSoloQueue(solo, UiLanguage.IsEnglish);
        }
        catch { }
        try
        {
            MatchHistoryResponse? history = await _matches.GetPageAsync(member.Puuid, 0, 20);
            var games = history?.Games?.Games.Where(game => game.IsCompletedGame()).ToArray() ?? [];
            var results = games.Select(game => game.GetParticipant(member.Puuid)?.stats)
                .Where(stats => stats != null).ToArray();
            if (results.Length > 0)
            {
                double winRate = results.Count(stats => stats!.Win) * 100d / results.Length;
                double kda = results.Sum(stats => stats!.kills + stats.assists) /
                    (double)Math.Max(1, results.Sum(stats => stats!.deaths));
                recent = UiLanguage.IsEnglish
                    ? $"Last {results.Length} · Win rate {winRate:0}% · KDA {kda:0.0}"
                    : $"近 {results.Length} 场 · 胜率 {winRate:0}% · KDA {kda:0.0}";
            }
        }
        catch { }
        return $"{name}\n{rank}\n{recent}";
    }

    private void FollowClient(Rectangle client)
    {
        Rectangle work = Screen.FromRectangle(client).WorkingArea;
        int width = Math.Clamp(client.Width / 3, 300, 420);
        int height = Math.Clamp(client.Height, 440, work.Height);
        int x = client.Right + width <= work.Right ? client.Right : client.Left - width;
        x = Math.Clamp(x, work.Left, work.Right - width);
        int y = Math.Clamp(client.Top, work.Top, work.Bottom - height);
        if (Bounds != new Rectangle(x, y, width, height)) Bounds = new Rectangle(x, y, width, height);
        float fontSize = Math.Clamp(width / 35f, 8f, 12f);
        if (Math.Abs(Font.Size - fontSize) > 0.25f) Font = new Font("Microsoft YaHei UI", fontSize);
        foreach (Control card in _cards.Controls) card.Width = Math.Max(200, _cards.ClientSize.Width - 20);
    }

    private static bool TryGetClientBounds(out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        try
        {
            foreach (Process process in Process.GetProcessesByName("LeagueClientUx"))
            {
                using (process)
                {
                    IntPtr handle = process.MainWindowHandle;
                    if (handle == IntPtr.Zero || IsIconic(handle) || !IsWindowVisible(handle) ||
                        !GetWindowRect(handle, out Rect rect)) continue;
                    bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                    if (bounds.Width > 500 && bounds.Height > 400) return true;
                }
            }
        }
        catch (InvalidOperationException) { /* The client exited during enumeration. */ }
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out Rect bounds);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);
}
