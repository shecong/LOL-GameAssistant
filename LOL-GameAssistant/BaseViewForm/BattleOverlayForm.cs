using System.Drawing.Drawing2D;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.LiveGame;
using LOL_GameAssistant.Application.Lobby;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Domain.LiveGame;
using LOL_GameAssistant.Domain.Ranked;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>A read-only, non-activating scoreboard which lets mouse input pass through to the game.</summary>
internal sealed class BattleOverlayForm : AntdUI.Window
{
    private readonly ILobbyService _lobby;
    private readonly BattleOverlayService _details;
    private readonly ILiveClientGameStateService _live;
    private readonly IGameAssetService _assets;
    private readonly IChampionCatalog _champions;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    private readonly Dictionary<string, BattleOverlayPlayer> _players = new();
    private readonly Dictionary<int, Image> _icons = new();
    private readonly HashSet<string> _loading = new();
    private readonly HashSet<int> _loadingIcons = new();
    private readonly Dictionary<string, DateTimeOffset> _retryAt = new();
    private CancellationTokenSource? _tracking;
    private IReadOnlyList<LiveScoreboardPlayer>? _scores;
    private ActiveGameSnapshot? _session;
    private string _signature = "";
    private string _hotkeyText = "·";
    private bool _refreshing;
    private int _generation;

    public BattleOverlayForm() : this(AppCompositionRoot.LobbyService, AppCompositionRoot.BattleOverlayService,
        AppCompositionRoot.LiveClientGameStateService, AppCompositionRoot.GameAssetService, AppCompositionRoot.ChampionCatalog) { }

    internal BattleOverlayForm(ILobbyService lobby, BattleOverlayService details, ILiveClientGameStateService live,
        IGameAssetService assets, IChampionCatalog champions)
    {
        _lobby = lobby; _details = details; _live = live; _assets = assets; _champions = champions;
        Text = "对局详情";
        AntdWindowChrome.Configure(this);
        EnableHitTest = false;
        ShowInTaskbar = false;
        TopMost = true;
        Opacity = .98; // A layered transparent window passes mouse input through across processes.
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(18, 24, 36);
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(1120, 560);
        _timer.Tick += (_, _) => _ = RefreshAsync();
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000000 | 0x00000080 | 0x00000020; // NOACTIVATE, TOOLWINDOW, TRANSPARENT
            return parameters;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0084) { m.Result = new IntPtr(-1); return; } // HTTRANSPARENT
        if (m.Msg == 0x0021) { m.Result = new IntPtr(3); return; } // MA_NOACTIVATE
        base.WndProc(ref m);
    }

    public void StartTracking()
    {
        if (_tracking != null || IsDisposed) return;
        _tracking = new CancellationTokenSource();
        _timer.Start();
        _ = RefreshAsync();
    }

    public void StopTracking()
    {
        Hide();
        _timer.Stop();
        _generation++;
        _tracking?.Cancel();
        _tracking?.Dispose();
        _tracking = null;
        _session = null;
        _signature = "";
        _scores = null;
        _players.Clear();
        _loading.Clear();
        _retryAt.Clear();
        Invalidate();
    }

    public void ShowForGame(Rectangle gameBounds, string hotkey)
    {
        _hotkeyText = hotkey;
        var area = Rectangle.Intersect(gameBounds, Screen.FromRectangle(gameBounds).Bounds);
        if (area.Width <= 0 || area.Height <= 0) area = Screen.FromRectangle(gameBounds).Bounds;
        float scale = Math.Min(1.15f, Math.Min(area.Width * .90f / 1120, area.Height * .85f / 560));
        Size = new Size((int)(1120 * scale), (int)(560 * scale));
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
        Show();
        Invalidate();
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_refreshing || _tracking == null || IsDisposed) return;
        _refreshing = true;
        CancellationToken token = _tracking.Token;
        try
        {
            var session = await _lobby.GetCurrentSessionAsync(token).WaitAsync(TimeSpan.FromSeconds(5), token);
            if (token.IsCancellationRequested || IsDisposed) return;
            if (session != null)
            {
                string signature = $"{session.QueueId}:{session.GameMode}:" + string.Join("|",
                    session.TeamOne.Concat(session.TeamTwo).Select(m => $"{m.Puuid}:{m.SummonerName}:{m.ChampionId}"));
                if (signature != _signature)
                {
                    _generation++;
                    _signature = signature;
                    _players.Clear(); _loading.Clear(); _retryAt.Clear(); _scores = null;
                }
                _session = session;
                foreach (var member in session.TeamOne.Concat(session.TeamTwo))
                {
                    string key = MemberKey(member);
                    if (!_loading.Contains(key) && (!_players.ContainsKey(key) ||
                        (_retryAt.TryGetValue(key, out var retry) && retry <= DateTimeOffset.UtcNow)))
                    {
                        _loading.Add(key);
                        _ = LoadPlayerAsync(member, session, _generation, token);
                    }
                    if (member.ChampionId > 0 && !_icons.ContainsKey(member.ChampionId) && _loadingIcons.Add(member.ChampionId))
                        _ = LoadIconAsync(member.ChampionId, token);
                }
            }
            if (Visible)
            {
                var scores = await _live.GetScoreboardAsync(token).WaitAsync(TimeSpan.FromSeconds(5), token);
                if (token.IsCancellationRequested || IsDisposed) return;
                _scores = scores;
            }
            Invalidate();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested && !IsDisposed)
            {
                _scores = null;
                RuntimeDiagnostics.Report("对局信息面板", "等待数据", ex.Message);
                Invalidate();
            }
        }
        finally { _refreshing = false; }
    }

    private static string MemberKey(GameTeamMember member) =>
        $"{member.Puuid}:{member.SummonerName}:{member.ChampionId}";

    private async Task LoadPlayerAsync(GameTeamMember member, ActiveGameSnapshot session, int generation, CancellationToken token)
    {
        try
        {
            BattleOverlayPlayer player = await _details.GetPlayerAsync(member, session, token);
            if (token.IsCancellationRequested || generation != _generation || IsDisposed) return;
            string key = MemberKey(member);
            _players[key] = player;
            if (!member.IsBot && !string.IsNullOrWhiteSpace(member.Puuid) &&
                (player.Rank == null || player.RecentResults.Count == 0))
                _retryAt[key] = DateTimeOffset.UtcNow.AddSeconds(30);
            else _retryAt.Remove(key);
            Invalidate();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { RuntimeDiagnostics.Report("对局玩家资料", "等待数据", ex.Message); }
        finally { if (generation == _generation) _loading.Remove(MemberKey(member)); }
    }

    private async Task LoadIconAsync(int championId, CancellationToken token)
    {
        try
        {
            var asset = await _assets.GetChampionIconAsync(championId, token).WaitAsync(TimeSpan.FromSeconds(5), token);
            if (token.IsCancellationRequested || IsDisposed || asset == null || asset.IsEmpty) return;
            using var stream = new MemoryStream(asset.Content);
            using var image = Image.FromStream(stream);
            _icons[championId] = new Bitmap(image);
            Invalidate();
        }
        catch { /* Names still render when artwork is unavailable. */ }
        finally { _loadingIcons.Remove(championId); }
    }

    internal void SetPreviewData(ActiveGameSnapshot session, IEnumerable<BattleOverlayPlayer> players,
        IReadOnlyList<LiveScoreboardPlayer>? scores = null)
    {
        _session = session;
        _players.Clear();
        foreach (var player in players) _players[MemberKey(player.Member)] = player;
        _scores = scores;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.ScaleTransform(ClientSize.Width / 1120f, ClientSize.Height / 560f);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var border = new Pen(Color.FromArgb(65, 84, 110));
        g.DrawRectangle(border, 0, 0, 1119, 559);
        bool en = UiLanguage.IsEnglish;
        DrawText(g, en ? "MATCH DETAILS" : "对局详情", new RectangleF(26, 14, 620, 34), 19, Color.White, true);
        DrawText(g, en ? $"Hold {_hotkeyText} to view · Release to hide" : $"按住 {_hotkeyText} 查看 · 松开隐藏",
            new RectangleF(685, 20, 410, 24), 10, Color.FromArgb(167, 182, 204));
        DrawTeam(g, _session?.TeamOne ?? [], 22, true, en);
        DrawTeam(g, _session?.TeamTwo ?? [], 572, false, en);
        if (_session == null || _session.TeamOne.Count + _session.TeamTwo.Count == 0)
            DrawText(g, en ? "Waiting for the game roster…" : "正在等待本局阵容，进入对局后自动加载…",
                new RectangleF(300, 250, 600, 50), 14, Color.LightGray);
        string queue = _session?.QueueId == 440 ? (en ? "Flex" : "灵活组排") : (en ? "Solo/Duo" : "单双排");
        DrawText(g, en ? $"Rank / season record: {queue} · Champion rate: last 100 games in this mode · Recent: up to 10 completed games"
            : $"段位 / 赛季战绩：{queue}  ·  英雄胜率：最近100场中的同模式样本  ·  近期：最多10场有效对局",
            new RectangleF(26, 510, 1070, 22), 9, Color.FromArgb(147, 164, 188));
        DrawText(g, en ? "Vision is vision score; ward placement counts are unavailable. Missing data is shown as —."
            : "视野为视野得分；客户端未提供实时插眼数量。缺失数据以 — 显示。",
            new RectangleF(26, 534, 1070, 20), 9, Color.FromArgb(127, 143, 165));
    }

    private void DrawTeam(Graphics g, IReadOnlyList<GameTeamMember> members, int x, bool blue, bool en)
    {
        Color accent = blue ? Color.FromArgb(85, 173, 255) : Color.FromArgb(255, 116, 136);
        using var heading = new SolidBrush(blue ? Color.FromArgb(27, 51, 77) : Color.FromArgb(65, 34, 48));
        g.FillRectangle(heading, x, 62, 526, 33);
        DrawText(g, blue ? (en ? "BLUE TEAM" : "蓝方") : (en ? "RED TEAM" : "红方"),
            new RectangleF(x + 12, 65, 420, 26), 12, accent, true);
        DrawText(g, en ? "Player / Champion" : "玩家 / 英雄", new RectangleF(x + 10, 101, 162, 22), 9, Color.LightSlateGray);
        DrawText(g, en ? "Rank / Season" : "段位 / 赛季", new RectangleF(x + 186, 101, 100, 22), 9, Color.LightSlateGray);
        DrawText(g, en ? "Recent / KDA" : "近期 / KDA", new RectangleF(x + 299, 101, 110, 22), 9, Color.LightSlateGray);
        DrawText(g, en ? "Live / Vision" : "本局 / 视野", new RectangleF(x + 422, 101, 105, 22), 9, Color.LightSlateGray);
        for (int index = 0; index < Math.Max(5, members.Count); index++)
        {
            // Two-team modes with more than five players fit in the same panel.
            int rows = Math.Max(5, members.Count);
            float rowHeight = 370f / rows;
            float y = 126 + index * rowHeight;
            if (index >= members.Count) continue;
            GameTeamMember member = members[index];
            _players.TryGetValue(MemberKey(member), out var player);
            using var background = new SolidBrush(index % 2 == 0 ? Color.FromArgb(25, 33, 48) : Color.FromArgb(21, 29, 43));
            g.FillRectangle(background, x, y, 526, rowHeight - 3);
            float iconSize = Math.Min(43, rowHeight - 12);
            if (_icons.TryGetValue(member.ChampionId, out var icon)) g.DrawImage(icon, x + 10, y + 10, iconSize, iconSize);
            else { using var placeholder = new SolidBrush(Color.FromArgb(43, 57, 78)); g.FillRectangle(placeholder, x + 10, y + 10, iconSize, iconSize); }
            DrawText(g, player?.Name ?? member.SummonerName, new RectangleF(x + 61, y + 7, 119, 23), 10, Color.White, true);
            DrawText(g, member.ChampionId > 0 ? _champions.GetDisplayName(member.ChampionId) : (en ? "Unknown champion" : "英雄待获取"),
                new RectangleF(x + 61, y + 29, 119, 20), 9, accent);
            DrawText(g, player?.ChampionWinRate is double wr ? $"{wr:0}% · {player.ChampionGames}{(en ? " games" : "场")}" : "—",
                new RectangleF(x + 61, y + 50, 119, 18), 8, Color.LightSlateGray);
            DrawText(g, member.IsBot ? (en ? "Bot" : "机器人") : player?.Rank == null ? "—" : RankedDisplayFormatter.FormatCompact(player.Rank, en),
                new RectangleF(x + 186, y + 9, 111, 24), 10, Color.FromArgb(234, 205, 145));
            DrawText(g, player?.Rank is { TotalGames: > 0 } rank ? $"{rank.WinRate:0}% · {rank.TotalGames}{(en ? " games" : "场")}" : "—",
                new RectangleF(x + 186, y + 36, 111, 22), 9, Color.LightSlateGray);
            if (player?.RecentResults is { Count: > 0 } results)
                for (int i = 0; i < results.Count; i++)
                {
                    using var brush = new SolidBrush(results[i] ? Color.FromArgb(63, 183, 149) : Color.FromArgb(223, 96, 114));
                    g.FillRectangle(brush, x + 299 + i * 10, y + 15, 7, 13);
                }
            else DrawText(g, "—", new RectangleF(x + 299, y + 9, 110, 22), 10, Color.LightSlateGray);
            DrawText(g, player?.RecentKda is double kda ? $"KDA {kda:0.00}" : "KDA —",
                new RectangleF(x + 299, y + 36, 110, 22), 9, Color.LightGray);
            var scores = FindScore(_scores, player?.Name ?? member.SummonerName, member.SummonerName, blue ? "ORDER" : "CHAOS");
            DrawText(g, scores == null ? "— / — / —" : $"{scores.Kills} / {scores.Deaths} / {scores.Assists}",
                new RectangleF(x + 422, y + 9, 103, 22), 10, Color.White);
            DrawText(g, scores == null ? "—" : $"{(en ? "CS" : "补刀")} {scores.CreepScore} · {(en ? "VS" : "视野")} {scores.VisionScore?.ToString("0") ?? "—"}",
                new RectangleF(x + 422, y + 36, 103, 24), 8, Color.LightSlateGray);
        }
    }

    internal static LiveScoreboardPlayer? FindScore(IReadOnlyList<LiveScoreboardPlayer>? scores, string name, string fallback, string team)
    {
        // Exact Riot IDs first; only accept a unique legacy-name match in the expected team.
        var exact = scores?.Where(p => p.Team == team && !string.IsNullOrWhiteSpace(p.RiotId) &&
            string.Equals(p.RiotId, name, StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
        if (exact.Length == 1) return exact[0];
        var legacy = scores?.Where(p => p.Team == team && !string.IsNullOrWhiteSpace(fallback) &&
            string.Equals(p.SummonerName, fallback, StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
        return legacy.Length == 1 ? legacy[0] : null;
    }

    private static void DrawText(Graphics g, string text, RectangleF bounds, float size, Color color, bool bold = false)
    {
        using var font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString(text, font, brush, bounds, format);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopTracking();
            _timer.Dispose();
            foreach (var image in _icons.Values) image.Dispose();
            _icons.Clear();
        }
        base.Dispose(disposing);
    }
}
