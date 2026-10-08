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
    private const int DesignWidth = 1120;
    private const int DesignHeight = 680;
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
    private readonly Dictionary<string, Image> _loadoutIcons = new();
    private readonly HashSet<string> _loadingLoadoutIcons = new();
    private readonly Dictionary<string, DateTimeOffset> _loadoutIconRetryAt = new();
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
        ClientSize = new Size(DesignWidth, DesignHeight);
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
        float scale = Math.Min(1.15f, Math.Min(area.Width * .90f / DesignWidth, area.Height * .85f / DesignHeight));
        Size = new Size((int)(DesignWidth * scale), (int)(DesignHeight * scale));
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
            // Read local loadouts independently: a slow LCU roster must not block live spells/runes.
            var sessionTask = ReadSessionAsync(token);
            if (Visible)
            {
                var liveScores = await _live.GetScoreboardAsync(token).WaitAsync(TimeSpan.FromSeconds(5), token);
                if (token.IsCancellationRequested || IsDisposed) return;
                _scores = liveScores;
                if (_scores != null)
                {
                    EnsureLoadoutIcons(_scores, token);
                    if (_session == null) _session = CreateLiveRoster(_scores);
                }
                Invalidate();
            }
            var session = await sessionTask;
            if (token.IsCancellationRequested || IsDisposed) return;
            if (session != null && session.TeamOne.Count + session.TeamTwo.Count > 0)
            {
                string signature = $"{session.QueueId}:{session.GameMode}:" + string.Join("|",
                    session.TeamOne.Concat(session.TeamTwo).Select(m => $"{m.Puuid}:{m.SummonerName}:{m.ChampionId}"));
                if (signature != _signature)
                {
                    _generation++;
                    _signature = signature;
                    _players.Clear(); _loading.Clear(); _retryAt.Clear();
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

    private async Task<ActiveGameSnapshot?> ReadSessionAsync(CancellationToken token)
    {
        try { return await _lobby.GetCurrentSessionAsync(token).WaitAsync(TimeSpan.FromSeconds(5), token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return null; }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested && !IsDisposed)
                RuntimeDiagnostics.Report("对局阵容", "等待客户端数据", ex.Message);
            return null;
        }
    }

    private ActiveGameSnapshot CreateLiveRoster(IReadOnlyList<LiveScoreboardPlayer> scores)
    {
        GameTeamMember Member(LiveScoreboardPlayer player) => new()
        {
            SummonerName = string.IsNullOrWhiteSpace(player.RiotId) ? player.SummonerName : player.RiotId,
            ChampionId = _champions.FindIdByDisplayName(player.ChampionName) ?? 0, IsBot = player.IsBot
        };
        return new() { TeamOne = scores.Where(p => p.Team == "ORDER").Select(Member).ToArray(),
            TeamTwo = scores.Where(p => p.Team == "CHAOS").Select(Member).ToArray() };
    }

    private void EnsureLoadoutIcons(IReadOnlyList<LiveScoreboardPlayer> scores, CancellationToken token)
    {
        foreach (var loadout in scores.Select(p => p.Loadout).OfType<LiveGameLoadout>())
        {
            foreach (var rune in new[] { loadout.Keystone, loadout.PrimaryRuneTree, loadout.SecondaryRuneTree })
                EnsureLoadoutIcon(rune, true, token);
            EnsureLoadoutIcon(loadout.SpellOne, false, token);
            EnsureLoadoutIcon(loadout.SpellTwo, false, token);
        }
    }

    private void EnsureLoadoutIcon(LiveGameAbility? ability, bool rune, CancellationToken token)
    {
        if (ability is not { Id: > 0 }) return;
        string key = $"{(rune ? "rune" : "spell")}:{ability.Id}";
        if (_loadoutIcons.ContainsKey(key) || _loadingLoadoutIcons.Contains(key) ||
            (_loadoutIconRetryAt.TryGetValue(key, out var retry) && retry > DateTimeOffset.UtcNow)) return;
        _loadingLoadoutIcons.Add(key);
        _ = LoadLoadoutIconAsync(ability.Id, rune, key, token);
    }

    private async Task LoadLoadoutIconAsync(int id, bool rune, string key, CancellationToken token)
    {
        try
        {
            var asset = await (rune ? _assets.GetRuneIconAsync(id, token) : _assets.GetSummonerSpellIconAsync(id, token))
                .WaitAsync(TimeSpan.FromSeconds(5), token);
            if (token.IsCancellationRequested || IsDisposed) return;
            if (asset == null || asset.IsEmpty) { _loadoutIconRetryAt[key] = DateTimeOffset.UtcNow.AddSeconds(30); return; }
            using var stream = new MemoryStream(asset.Content);
            using var image = Image.FromStream(stream);
            _loadoutIcons[key] = new Bitmap(image);
            _loadoutIconRetryAt.Remove(key);
            Invalidate();
        }
        catch { if (!token.IsCancellationRequested && !IsDisposed) _loadoutIconRetryAt[key] = DateTimeOffset.UtcNow.AddSeconds(30); }
        finally { _loadingLoadoutIcons.Remove(key); }
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
        g.ScaleTransform(ClientSize.Width / (float)DesignWidth, ClientSize.Height / (float)DesignHeight);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var border = new Pen(Color.FromArgb(65, 84, 110));
        g.DrawRectangle(border, 0, 0, DesignWidth - 1, DesignHeight - 1);
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
            new RectangleF(26, 630, 1070, 22), 9, Color.FromArgb(147, 164, 188));
        DrawText(g, en ? "Vision is vision score; ward placement counts are unavailable. Missing data is shown as —."
            : "视野为视野得分；客户端未提供实时插眼数量。缺失数据以 — 显示。",
            new RectangleF(26, 654, 1070, 20), 9, Color.FromArgb(127, 143, 165));
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
            float rowHeight = 490f / rows;
            if (index >= members.Count) continue;
            var rowState = g.Save();
            g.TranslateTransform(0, 126 + index * rowHeight);
            g.ScaleTransform(1, rowHeight / 98f);
            float y = 0;
            GameTeamMember member = members[index];
            _players.TryGetValue(MemberKey(member), out var player);
            using var background = new SolidBrush(index % 2 == 0 ? Color.FromArgb(25, 33, 48) : Color.FromArgb(21, 29, 43));
            g.FillRectangle(background, x, y, 526, 95);
            float iconSize = 43;
            if (_icons.TryGetValue(member.ChampionId, out var icon)) g.DrawImage(icon, x + 10, y + 10, iconSize, iconSize);
            else { using var placeholder = new SolidBrush(Color.FromArgb(43, 57, 78)); g.FillRectangle(placeholder, x + 10, y + 10, iconSize, iconSize); }
            DrawText(g, player?.Name ?? member.SummonerName, new RectangleF(x + 61, y + 7, 119, 23), 10, Color.White, true);
            DrawText(g, member.ChampionId > 0 ? _champions.GetDisplayName(member.ChampionId) : (en ? "Unknown champion" : "英雄待获取"),
                new RectangleF(x + 61, y + 29, 119, 20), 9, accent);
            DrawText(g, ChampionRateText(player, en),
                new RectangleF(x + 61, y + 49, 119, 12), 7, Color.LightSlateGray);
            DrawText(g, player is { ChampionGames: > 0 } ? RecordCounts(player.ChampionWins, player.ChampionLosses, en) : "—",
                new RectangleF(x + 61, y + 61, 119, 11), 7, Color.LightSlateGray);
            DrawText(g, member.IsBot ? (en ? "Bot" : "机器人") : player?.Rank == null ? "—" : RankedDisplayFormatter.FormatCompact(player.Rank, en),
                new RectangleF(x + 186, y + 9, 111, 24), 10, Color.FromArgb(234, 205, 145));
            DrawText(g, RankRateText(player?.Rank, en),
                new RectangleF(x + 186, y + 35, 111, 18), 8, Color.LightSlateGray);
            DrawText(g, player?.Rank is { TotalGames: > 0 } rank ? RecordCounts(rank.Wins, rank.Losses, en) : "—",
                new RectangleF(x + 186, y + 53, 111, 18), 7, Color.LightSlateGray);
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
            DrawLoadout(g, scores?.Loadout, x + 10, y + 72, en);
            g.Restore(rowState);
        }
    }

    internal static string ChampionRateText(BattleOverlayPlayer? player, bool en) =>
        player?.ChampionWinRate is double rate ? $"{(en ? "Sample" : "样本")} {rate:0.#}%" : "—";

    internal static string RankRateText(RankedQueue? rank, bool en)
    {
        if (rank is not { TotalGames: > 0 }) return "—";
        string queue = rank.QueueType == RankedQueues.Flex5x5 ? (en ? "Flex" : "灵活") : (en ? "Solo" : "单双");
        return $"{queue} {rank.WinRate:0.#}%";
    }

    internal static string RecordCounts(int wins, int losses, bool en) => en
        ? $"{wins}W {losses}L / {wins + losses}G" : $"{wins}胜{losses}负 / {wins + losses}场";

    private void DrawLoadout(Graphics g, LiveGameLoadout? loadout, float x, float y, bool en)
    {
        if (loadout?.Keystone == null && loadout?.PrimaryRuneTree == null && loadout?.SecondaryRuneTree == null)
        {
            bool noRunes = _session?.QueueId is 2400 or 3270 || (_session?.GameMode.StartsWith("KIWI", StringComparison.OrdinalIgnoreCase) ?? false);
            DrawText(g, noRunes ? (en ? "No standard runes in this mode" : "此模式无常规符文") : (en ? "Runes unavailable" : "天赋未提供"),
                new RectangleF(x, y, 250, 20), 8, Color.LightSlateGray);
        }
        else
        {
            DrawText(g, en ? "Runes" : "天赋", new RectangleF(x, y, 32, 20), 8, Color.LightSlateGray);
            DrawAbility(g, loadout.Keystone, true, x + 33, y, 101);
            DrawAbility(g, loadout.PrimaryRuneTree, true, x + 140, y, 62);
            DrawAbility(g, loadout.SecondaryRuneTree, true, x + 205, y, 62);
        }
        DrawText(g, en ? "Spells" : "技能", new RectangleF(x + 272, y, 34, 20), 8, Color.LightSlateGray);
        DrawAbility(g, loadout?.SpellOne, false, x + 308, y, 91);
        DrawAbility(g, loadout?.SpellTwo, false, x + 406, y, 96);
    }

    private void DrawAbility(Graphics g, LiveGameAbility? ability, bool rune, float x, float y, float width)
    {
        string key = $"{(rune ? "rune" : "spell")}:{ability?.Id ?? 0}";
        if (_loadoutIcons.TryGetValue(key, out var image)) g.DrawImage(image, x, y, 18, 18);
        else
        {
            using var brush = new SolidBrush(Color.FromArgb(43, 57, 78));
            g.FillRectangle(brush, x, y, 18, 18);
        }
        DrawText(g, string.IsNullOrWhiteSpace(ability?.Name) ? "—" : ability.Name,
            new RectangleF(x + 21, y, width - 21, 20), 8, Color.FromArgb(189, 203, 223));
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
            foreach (var image in _loadoutIcons.Values) image.Dispose();
            _loadoutIcons.Clear();
        }
        base.Dispose(disposing);
    }
}
