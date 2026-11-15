using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.Settings;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>完整字段的三种紧凑排版；切换只改变绘制，不重新读取对局。</summary>
internal sealed class MatchDetailStatisticsView : Control, IThemeAware
{
    private readonly IGameAssetService _assets;
    private readonly IChampionCatalog _champions;
    private readonly UiToolTip _tips = new();
    private readonly Dictionary<(string Kind, int Id), string> _names = [];
    private readonly List<(Rectangle Bounds, string Text, string? Puuid)> _hits = [];
    private MatchRowImages? _images;
    private CancellationTokenSource _lifetime = new();
    private MatchDetail? _detail;
    private string _viewer = "";
    private ThemePalette _palette = UiTheme.Palette;
    private long _version;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal MatchDetailLayout LayoutMode { get; set; }
    internal IReadOnlyList<MatchParticipant> Players { get; private set; } = [];
    internal IReadOnlyList<string> Honors => Players.Select(p => MatchHonorCalculator.ForPlayer(_detail!, p).Text).ToArray();
    internal event Action<string>? PlayerRequested;
    private int S(int n) => n * DeviceDpi / 96;
    internal int MinimumContentWidth => S(LayoutMode switch
    {
        MatchDetailLayout.Matrix => Math.Max(900, 120 + Players.Count * 88),
        MatchDetailLayout.Dense => 920,
        _ => 1000
    });
    internal MatchDetailStatisticsView(IGameAssetService assets, IChampionCatalog champions)
    {
        _assets = assets; _champions = champions;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Font = new Font(UiMetrics.FontFamily, 9F);
        AccessibleRole = AccessibleRole.Table;
        ApplyTheme(_palette);
    }
    internal void SetDetail(MatchDetail detail, string viewer)
    {
        _version++; _lifetime.Cancel(); _lifetime.Dispose(); _lifetime = new();
        _images?.Dispose(); _images = new(_assets, () => { if (!IsDisposed) Invalidate(); });
        _detail = detail; _viewer = viewer;
        int? ownTeam = detail.GetParticipant(viewer)?.teamId;
        Players = detail.participants.OrderBy(p => p.teamId == ownTeam ? 0 : 1).ThenBy(p => p.teamId).ThenBy(p => p.participantId).ToArray();
        AccessibleName = string.Join("\n", Players.Select(p => Identity(p) + " " + string.Join(" · ", Metrics(p).Select(m => m.Label + " " + m.Value)) + " " + Extras(p)));
        foreach (var p in Players)
        {
            Load("champion", p.championId); Load("spell", p.Spell1Id); Load("spell", p.Spell2Id);
            if (p.stats is not { } stats) continue;
            foreach (int id in Items(stats)) Load("item", id);
            foreach (int id in stats.RuneIds) Load("rune", id);
        }
        if (detail.HasAugments()) _ = ResolveAugmentsAsync(Players.SelectMany(p => p.stats?.AugmentIds ?? []).Distinct().ToArray(), _version);
        Invalidate();
    }
    private void Load(string kind, int id)
    {
        if (id <= 0) return;
        _ = _images!.LoadAsync(kind, id);
        if (kind != "champion" && !_names.ContainsKey((kind, id))) _ = ResolveNameAsync(kind, id, _version, _lifetime.Token);
    }
    private async Task ResolveNameAsync(string kind, int id, long version, CancellationToken token)
    {
        try
        {
            string? name = kind switch
            {
                "spell" => await _assets.GetSummonerSpellNameAsync(id, token),
                "rune" => await _assets.GetRuneDetailsAsync(id, token),
                _ => await _assets.GetItemNameAsync(id, token)
            };
            if (!IsDisposed && version == _version && !string.IsNullOrWhiteSpace(name)) { _names[(kind, id)] = name; Invalidate(); }
        }
        catch (Exception error) when (error is OperationCanceledException or HttpRequestException or IOException or InvalidOperationException) { }
    }
    private async Task ResolveAugmentsAsync(IReadOnlyList<int> ids, long version)
    {
        try
        {
            var resolved = await LOL_GameAssistant.Infrastructure.GameData.AugmentCatalog.ResolveAsync(ids);
            if (!IsDisposed && version == _version)
            {
                foreach (var augment in resolved) _names[("augment", augment.Id)] = augment.Name;
                Invalidate();
            }
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidOperationException) { }
    }
    private static int[] Items(MatchParticipantStats s) => [s.item0, s.item1, s.item2, s.item3, s.item4, s.item5, s.item6];
    private string DisplayName(MatchParticipant p)
    {
        var id = _detail!.participantIdentities.FirstOrDefault(i => i.participantId == p.participantId)?.player;
        string name = id?.GameName.Length > 0 ? id.GameName : id?.SummonerName.Length > 0 ? id.SummonerName : "身份未提供";
        return (id?.Puuid == _viewer ? "我 · " : "") + name;
    }
    private string Identity(MatchParticipant p)
    {
        var id = _detail!.participantIdentities.FirstOrDefault(i => i.participantId == p.participantId)?.player;
        return $"{DisplayName(p)}{(id?.TagLine.Length > 0 ? "#" + id.TagLine : "")} · {_champions.GetDisplayName(p.championId)} · 等级 {p.stats?.champLevel.ToString() ?? "—"}";
    }
    internal static (string Label, string Value)[] Metrics(MatchParticipant p)
    {
        var s = p.stats;
        string Number(int? value) => value?.ToString("N0") ?? "—";
        return [("K / D / A", s?.HasKdaResult == true ? p.GetKdaText() : "—"),
            ("伤害", s?.HasDamageResult == true ? Number(s.totalDamageDealtToChampions) : "—"),
            ("承伤", Number(s?.totalDamageTaken)), ("治疗", Number(s?.totalHeal)), ("金币", Number(s?.goldEarned)),
            ("补刀", s?.HasFarmResult == true ? Number(s.totalMinionsKilled + s.neutralMinionsKilled) : "—"),
            ("视野", s?.HasVisionResult == true ? Number(s.visionScore) : "—"), ("控制", s == null ? "—" : s.totalTimeCrowdControlDealt + "s")];
    }
    private string Extras(MatchParticipant p)
    {
        var s = p.stats;
        if (s == null) return "本局统计未提供";
        string farm = s.HasFarmResult ? $"线上 {s.totalMinionsKilled} / 野怪 {s.neutralMinionsKilled} · CS/分 {(s.totalMinionsKilled + s.neutralMinionsKilled) / Math.Max(1, (_detail?.gameDuration ?? 0) / 60d):0.0}" : "补刀未提供";
        string honors = MatchHonorCalculator.ForPlayer(_detail!, p).Text;
        return $"{farm} · 连杀 {s.largestKillingSpree} · 多杀 {s.doubleKills}/{s.tripleKills}/{s.quadraKills}/{s.pentaKills}" + (honors.Length > 0 ? " · " + honors : "");
    }
    private IGrouping<int, MatchParticipant>[] Teams => Players.GroupBy(p => p.teamId).ToArray();
    private bool UsePairs(int width) => LayoutMode == MatchDetailLayout.Paired && Teams.Length == 2 && width >= S(1000);
    private int InventoryHeight(MatchParticipant p, int width)
    {
        int icons = 9 + (p.stats?.RuneIds.Count ?? 0);
        return Math.Max(1, (icons + Math.Max(1, width / S(25)) - 1) / Math.Max(1, width / S(25))) * S(26);
    }
    private int ExtraHeight(MatchParticipant p, int width)
    {
        using var font = new Font(UiMetrics.FontFamily, 8.25F);
        return Math.Max(S(23), TextRenderer.MeasureText(Extras(p), font, new Size(Math.Max(S(100), width - S(12)), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + S(4));
    }
    private int PlayerHeight(MatchParticipant p, int width, bool paired) =>
        S(paired ? 80 : 46) + InventoryHeight(p, Math.Max(S(100), width - S(16))) + AugmentHeight(p, width) + ExtraHeight(p, width);
    private int AugmentHeight(MatchParticipant p, int width) => p.stats?.AugmentIds.Count > 0
        ? (int)Math.Ceiling(p.stats.AugmentIds.Count / (double)Math.Max(1, width / S(180))) * S(22) : 0;
    private int MatrixHonorHeight(int cell)
    {
        using var font = new Font(UiMetrics.FontFamily, 8.25F);
        return Math.Max(S(25), Players.Select(p => TextRenderer.MeasureText(MatchHonorCalculator.ForPlayer(_detail!, p).Text, font,
            new Size(Math.Max(1, cell), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + S(4)).DefaultIfEmpty(S(25)).Max());
    }
    internal int MeasureContentHeight(int width)
    {
        if (_detail == null) return S(80);
        int y = S(32);
        if (LayoutMode == MatchDetailLayout.Matrix)
        {
            int cell = Math.Max(S(70), (width - S(120)) / Math.Max(1, Players.Count));
            y += S(100 + 13 * 25) + MatrixHonorHeight(cell) + Math.Max(1, (7 + Math.Max(1, cell / S(25)) - 1) / Math.Max(1, cell / S(25))) * S(26);
            y += Math.Max(1, (Players.Max(p => p.stats?.RuneIds.Count ?? 0) + Math.Max(1, cell / S(25)) - 1) / Math.Max(1, cell / S(25))) * S(26);
            if (_detail.HasAugments()) y += Math.Max(1, Players.Max(p => p.stats?.AugmentIds.Count ?? 0)) * S(22);
        }
        else if (UsePairs(width))
        {
            var teams = Teams; var first = teams[0].ToArray(); var second = teams[1].ToArray();
            int cell = (width - S(8)) / 2;
            y += S(28);
            for (int i = 0; i < Math.Max(first.Length, second.Length); i++)
                y += Math.Max(i < first.Length ? PlayerHeight(first[i], cell, true) : 0, i < second.Length ? PlayerHeight(second[i], cell, true) : 0);
        }
        else foreach (var team in Teams) y += S(48) + team.Sum(p => PlayerHeight(p, width, false));
        return y + Teams.Length * S(26) + S(30);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); _hits.Clear();
        if (_detail == null || Players.Count == 0) return;
        using var font = new Font(UiMetrics.FontFamily, 9F);
        using var small = new Font(UiMetrics.FontFamily, 8.25F);
        using var bold = new Font(UiMetrics.FontFamily, 9F, FontStyle.Bold);
        var g = e.Graphics;
        DrawText(g, $"对局 {_detail.GameId} · {_detail.PlatformId} · 队列 {_detail.queueId} · {_detail.gameCreationDate ?? "时间未提供"} · {_detail.participants.Count} 人 · {(_detail.HasFullParticipants ? "完整参赛者" : "已返回参赛者")}", new(4, 0, Width - 8, S(30)), small, _palette.TextSecondary);
        int y = S(32);
        if (LayoutMode == MatchDetailLayout.Matrix) y = DrawMatrix(g, y, font, small, bold);
        else if (UsePairs(Width))
        {
            var first = Teams[0].ToArray(); var second = Teams[1].ToArray(); int cell = (Width - S(8)) / 2;
            TeamHeading(g, Teams[0], new(0, y, cell, S(28)), bold); TeamHeading(g, Teams[1], new(cell + S(8), y, cell, S(28)), bold); y += S(28);
            for (int i = 0; i < Math.Max(first.Length, second.Length); i++)
            {
                int h = Math.Max(i < first.Length ? PlayerHeight(first[i], cell, true) : 0, i < second.Length ? PlayerHeight(second[i], cell, true) : 0);
                if (i < first.Length) DrawPlayer(g, first[i], new(0, y, cell, h), true, font, small, bold);
                if (i < second.Length) DrawPlayer(g, second[i], new(cell + S(8), y, cell, h), true, font, small, bold);
                y += h;
            }
        }
        else foreach (var team in Teams)
        {
            TeamHeading(g, team, new(0, y, Width, S(28)), bold); y += S(28);
            MetricHeader(g, new(0, y, Width, S(20)), small); y += S(20);
            foreach (var p in team) { int h = PlayerHeight(p, Width, false); DrawPlayer(g, p, new(0, y, Width, h), false, font, small, bold); y += h; }
        }
        foreach (var team in Teams)
        {
            string bans = string.Join(" · ", _detail.GetBannedChampionIds(team.Key).Select(_champions.GetDisplayName));
            DrawText(g, $"队伍 {team.Key} 禁用：{(bans.Length == 0 ? "未提供" : bans)}", new(S(6), y, Width - S(12), S(26)), small, _palette.TextSecondary); y += S(26);
        }
        DrawText(g, "多杀顺序：双 / 三 / 四 / 五；未提供字段显示 —。双击玩家查看战绩。", new(S(6), y, Width - S(12), S(26)), small, _palette.TextSecondary);
    }
    private void TeamHeading(Graphics g, IGrouping<int, MatchParticipant> team, Rectangle bounds, Font font)
    {
        bool own = team.Key == _detail!.GetParticipant(_viewer)?.teamId;
        using var fill = new SolidBrush(_palette.SurfaceMuted); g.FillRectangle(fill, bounds);
        string teamName = Teams.Length > 2 ? $"队伍 {team.Key}" : own ? "我方" : "敌方";
        var result = team.Select(p => p.stats).FirstOrDefault(s => s?.HasWinResult == true);
        string outcome = result == null ? "胜负未提供" : result.Win ? "胜利" : "失败";
        DrawText(g, $"{teamName} · {outcome} · {team.Count()} 人 · {team.Sum(p => p.stats?.kills ?? 0)} 击杀 · {team.Sum(p => (long)(p.stats?.goldEarned ?? 0)):N0} 金币", bounds, font, own ? _palette.Accent : Color.FromArgb(220, 106, 123));
    }
    private void MetricHeader(Graphics g, Rectangle bounds, Font font)
    {
        int identity = S(210), cell = Math.Max(1, (bounds.Width - identity) / 8);
        DrawText(g, "玩家 / 英雄 / 等级", new(bounds.X + S(5), bounds.Y, identity, bounds.Height), font, _palette.TextSecondary);
        for (int i = 0; i < 8; i++) DrawText(g, Metrics(Players[0])[i].Label, new(bounds.X + identity + cell * i, bounds.Y, cell, bounds.Height), font, _palette.TextSecondary, true);
    }
    private void DrawPlayer(Graphics g, MatchParticipant p, Rectangle b, bool paired, Font font, Font small, Font bold)
    {
        bool self = _detail!.GetParticipant(_viewer)?.participantId == p.participantId;
        using var fill = new SolidBrush(self ? _palette.SurfaceMuted : _palette.SurfaceRaised); g.FillRectangle(fill, b);
        using var line = new Pen(_palette.Border); g.DrawLine(line, b.Left, b.Bottom - 1, b.Right, b.Bottom - 1);
        int x = b.X + S(6), y = b.Y + S(4), inner = b.Width - S(12);
        Icon(g, "champion", p.championId, new(x, y, S(30), S(30)));
        int identityWidth = paired ? Math.Max(S(180), inner - S(145)) : S(170);
        DrawText(g, DisplayName(p), new(x + S(35), y, identityWidth - S(35), S(17)), bold, _palette.TextPrimary);
        var id = _detail!.participantIdentities.FirstOrDefault(i => i.participantId == p.participantId)?.player;
        DrawText(g, $"{(id?.TagLine.Length > 0 ? "#" + id.TagLine + " · " : "")}{_champions.GetDisplayName(p.championId)} · Lv.{p.stats?.champLevel.ToString() ?? "—"}", new(x + S(35), y + S(17), identityWidth - S(35), S(17)), small, _palette.TextSecondary);
        _hits.Add((new(x, y, identityWidth, S(34)), Identity(p) + "\n" + id?.Puuid, id?.Puuid));
        var metrics = Metrics(p);
        if (paired)
        {
            DrawText(g, metrics[0].Value + (p.stats?.HasKdaResult == true ? $" ({p.GetKdaRatio():0.00})" : ""), new(b.Right - S(145), y, S(135), S(30)), bold, _palette.TextPrimary, true);
            y += S(36); int cell = inner / 7;
            for (int i = 1; i < 8; i++)
            {
                DrawText(g, metrics[i].Label, new(x + cell * (i - 1), y, cell, S(16)), small, _palette.TextSecondary);
                DrawText(g, metrics[i].Value, new(x + cell * (i - 1), y + S(16), cell, S(18)), font, _palette.TextPrimary);
            }
            y += S(36);
        }
        else
        {
            int cell = (b.Width - S(210)) / 8;
            for (int i = 0; i < 8; i++) DrawText(g, metrics[i].Value, new(b.X + S(210) + cell * i, y, cell, S(21)), i == 0 ? bold : font, _palette.TextPrimary, true);
            if (p.stats?.HasKdaResult == true) DrawText(g, $"KDA {p.GetKdaRatio():0.00}", new(b.X + S(210), y + S(20), cell, S(16)), small, _palette.TextSecondary, true);
            y += S(38);
        }
        y = Inventory(g, p, x, y, inner);
        y = Augments(g, p, x, y, inner, small);
        DrawText(g, Extras(p), new(x, y, inner, ExtraHeight(p, b.Width)), small, _palette.TextSecondary);
    }
    private int Inventory(Graphics g, MatchParticipant p, int x, int y, int width)
    {
        int start = x, right = x + width;
        var icons = new List<(string Kind, int Id)> { ("spell", p.Spell1Id), ("spell", p.Spell2Id) };
        icons.AddRange(Items(p.stats ?? new()).Select(id => ("item", id)));
        icons.AddRange((p.stats?.RuneIds ?? []).Select(id => ("rune", id)));
        foreach (var (kind, id) in icons)
        {
            if (x + S(23) > right) { x = start; y += S(26); }
            Icon(g, kind, id, new(x, y, S(22), S(22))); x += S(25);
        }
        return y + S(26);
    }
    private int Augments(Graphics g, MatchParticipant p, int x, int y, int width, Font font)
    {
        int start = x;
        foreach (int id in p.stats?.AugmentIds ?? [])
        {
            int w = S(180);
            if (x + w > start + width && x > start) { y += S(22); x = start; }
            DrawText(g, _names.GetValueOrDefault(("augment", id), "强化 #" + id), new(x, y, Math.Min(w, width), S(22)), font, _palette.Accent); x += w;
        }
        return p.stats?.AugmentIds.Count > 0 ? y + S(22) : y;
    }
    private void Icon(Graphics g, string kind, int id, Rectangle b)
    {
        var image = _images?.Get(kind, id);
        if (image != null) g.DrawImage(image, b);
        else
        {
            using var fill = new SolidBrush(_palette.SurfaceMuted); g.FillRectangle(fill, b);
            using var font = new Font(UiMetrics.FontFamily, 7F);
            DrawText(g, id <= 0 ? "—" : kind == "rune" && RuneStatShards.Find(id) is { } shard ? shard.ShortName : kind == "champion" ? _champions.GetDisplayName(id) is { Length: > 0 } name ? name[..1] : "?" : id.ToString(), b, font, _palette.TextSecondary, true);
        }
        string title = kind switch { "champion" => _champions.GetDisplayName(id), "spell" => "召唤师技能", "rune" => "符文", _ => "装备" };
        _hits.Add((b, id <= 0 ? title + " · 空栏 / 未提供" : _names.GetValueOrDefault((kind, id), kind == "rune" && RuneStatShards.Find(id) is { } known ? known.Details : title + " #" + id), null));
    }
    private int DrawMatrix(Graphics g, int y, Font font, Font small, Font bold)
    {
        int label = S(120), cell = (Width - label) / Math.Max(1, Players.Count);
        int xAt(int i) => label + i * cell;
        DrawText(g, "玩家 / 英雄", new(0, y, label, S(92)), font, _palette.TextSecondary);
        for (int i = 0; i < Players.Count; i++)
        {
            var p = Players[i]; var id = _detail!.participantIdentities.FirstOrDefault(v => v.participantId == p.participantId)?.player;
            DrawText(g, $"队伍 {p.teamId}\n{DisplayName(p)}\n{(id?.TagLine.Length > 0 ? "#" + id.TagLine : "")}\n{_champions.GetDisplayName(p.championId)} Lv.{p.stats?.champLevel.ToString() ?? "—"}", new(xAt(i), y, cell, S(92)), small, _palette.TextPrimary, true);
            _hits.Add((new(xAt(i), y, cell, S(92)), Identity(p), id?.Puuid));
        }
        y += S(100);
        var rows = new (string Label, Func<MatchParticipant, string> Value)[]
        {
            ("K / D / A", p => Metrics(p)[0].Value), ("KDA", p => p.stats?.HasKdaResult == true ? p.GetKdaRatio().ToString("0.00") : "—"),
            ("对英雄伤害", p => Metrics(p)[1].Value), ("承受伤害", p => Metrics(p)[2].Value), ("治疗", p => Metrics(p)[3].Value),
            ("金币", p => Metrics(p)[4].Value), ("补刀 / CS每分", p => p.stats?.HasFarmResult == true ? $"{Metrics(p)[5].Value} / {(p.stats.totalMinionsKilled + p.stats.neutralMinionsKilled) / Math.Max(1, _detail!.gameDuration / 60d):0.0}" : "—"),
            ("线上 / 野怪", p => p.stats?.HasFarmResult == true ? $"{p.stats.totalMinionsKilled} / {p.stats.neutralMinionsKilled}" : "—"),
            ("视野 / 控制", p => Metrics(p)[6].Value + " / " + Metrics(p)[7].Value),
            ("连杀峰值", p => p.stats?.largestKillingSpree.ToString() ?? "—"),
            ("双 / 三 / 四 / 五", p => p.stats is {} s ? $"{s.doubleKills}/{s.tripleKills}/{s.quadraKills}/{s.pentaKills}" : "—"),
            ("荣誉", p => MatchHonorCalculator.ForPlayer(_detail!, p).Text is { Length: > 0 } honors ? honors : "—"),
            ("技能 1", p => _names.GetValueOrDefault(("spell", p.Spell1Id), p.Spell1Id > 0 ? "#" + p.Spell1Id : "—").Split('\n')[0]),
            ("技能 2", p => _names.GetValueOrDefault(("spell", p.Spell2Id), p.Spell2Id > 0 ? "#" + p.Spell2Id : "—").Split('\n')[0])
        };
        foreach (var row in rows)
        {
            int rowHeight = row.Label == "荣誉" ? MatrixHonorHeight(cell) : S(25);
            DrawText(g, row.Label, new(0, y, label, rowHeight), small, _palette.TextSecondary);
            for (int i = 0; i < Players.Count; i++) DrawText(g, row.Value(Players[i]), new(xAt(i), y, cell, rowHeight), small, _palette.TextPrimary, true);
            using var line = new Pen(_palette.Border); g.DrawLine(line, 0, y + rowHeight, Width, y + rowHeight); y += rowHeight;
        }
        int DrawIcons(string title, Func<MatchParticipant, IReadOnlyList<int>> ids, string kind)
        {
            int cols = Math.Max(1, cell / S(25)); int h = Math.Max(1, (Players.Max(p => ids(p).Count) + cols - 1) / cols) * S(26);
            DrawText(g, title, new(0, y, label, h), font, _palette.TextSecondary);
            for (int i = 0; i < Players.Count; i++) for (int j = 0; j < ids(Players[i]).Count; j++)
                Icon(g, kind, ids(Players[i])[j], new(xAt(i) + j % cols * S(25), y + j / cols * S(26), S(22), S(22)));
            return h;
        }
        y += DrawIcons("七格装备", p => Items(p.stats ?? new()), "item");
        y += DrawIcons("全部符文", p => p.stats?.RuneIds ?? [], "rune");
        if (_detail!.HasAugments())
        {
            int h = Players.Max(p => p.stats?.AugmentIds.Count ?? 0) * S(22);
            DrawText(g, "强化", new(0, y, label, h), font, _palette.TextSecondary);
            for (int i = 0; i < Players.Count; i++)
                foreach (var (id, index) in (Players[i].stats?.AugmentIds ?? []).Select((id, index) => (id, index)))
                    DrawText(g, _names.GetValueOrDefault(("augment", id), "#" + id), new(xAt(i), y + index * S(22), cell, S(22)), small, _palette.Accent, true);
            y += h;
        }
        return y;
    }
    private void DrawText(Graphics g, string text, Rectangle bounds, Font font, Color color, bool center = false)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        TextRenderer.DrawText(g, text, font, bounds, color, TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter |
            TextFormatFlags.WordBreak | (center ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left));
        _hits.Add((bounds, text, null));
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = _hits.LastOrDefault(h => h.Bounds.Contains(e.Location));
        _tips.SetToolTip(this, hit.Text ?? "");
    }
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        string? puuid = _hits.LastOrDefault(h => h.Puuid != null && h.Bounds.Contains(e.Location)).Puuid;
        if (!string.IsNullOrWhiteSpace(puuid)) PlayerRequested?.Invoke(puuid);
    }
    public void ApplyTheme(ThemePalette palette) { _palette = palette; BackColor = palette.SurfaceRaised; ForeColor = palette.TextPrimary; Invalidate(); }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _version++; _lifetime.Cancel(); _lifetime.Dispose(); _images?.Dispose(); _tips.Dispose(); }
        base.Dispose(disposing);
    }
}
