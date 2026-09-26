using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Teams;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Teams;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.GameData;
using System.Collections.Concurrent;

namespace LOL_GameAssistant.BaseViewForm;

/// <summary>单局记录直接展示双方玩家、KDA、开黑标记、禁用英雄与强化。</summary>
public sealed class MatchHistoryCard : UserControl
{
    private const int HeaderHeight = 78;
    private static readonly SemaphoreSlim PremadeGate = new(2);
    private static readonly ConcurrentDictionary<(long GameId, int OwnTeamId), Task<PremadeDetectionResult>> PremadeCache = new();

    private readonly MatchDetail _match;
    private readonly string _viewerPuuid;
    private readonly IGameAssetService _assets;
    private readonly IPremadeDetectionService _premadeService;
    private readonly ToolTip _toolTip = new();
    private readonly AntdUI.Panel _surface = new() { Dock = DockStyle.Fill, Radius = 10, BorderWidth = 1 };
    private readonly Panel _teamHost = new() { BackColor = Color.Transparent };
    private readonly AntdUI.Panel _blueTeam;
    private readonly AntdUI.Panel _redTeam;
    private readonly List<PlayerRow> _blueRows = new();
    private readonly List<PlayerRow> _redRows = new();
    private readonly Dictionary<string, AntdUI.Tag> _premadeTags = new(StringComparer.Ordinal);
    private readonly Label _teamOneTitle;
    private readonly Label _teamTwoTitle;
    private readonly Label _modeTitle;
    private readonly Label _matchInfo;
    private readonly Label _kda;
    private readonly Label _duration;
    private readonly Label _result;
    private readonly AntdUI.Avatar _champion;
    private readonly AntdUI.Button _details;
    private bool _layoutBusy;

    private sealed record PlayerRow(
        AntdUI.Panel Panel,
        Label Name,
        Label Score,
        Label Detail,
        AntdUI.Tag Premade,
        IReadOnlyList<AntdUI.Tag> Augments);

    public MatchHistoryCard(MatchDetail match, string viewerPuuid, IGameAssetService assets)
        : this(match, viewerPuuid, assets, AppCompositionRoot.PremadeDetectionService)
    {
    }

    internal MatchHistoryCard(MatchDetail match, string viewerPuuid,
        IGameAssetService assets, IPremadeDetectionService premadeService)
    {
        _match = match;
        _viewerPuuid = viewerPuuid;
        _assets = assets;
        _premadeService = premadeService;
        BackColor = UiTheme.Palette.SurfaceMuted;
        MinimumSize = new Size(340, HeaderHeight + 100);
        Controls.Add(_surface);
        _surface.BackColor = UiTheme.Palette.SurfaceRaised;

        MatchParticipant? mine = match.GetParticipant(viewerPuuid);
        bool win = mine.IsWin();
        _result = MakeLabel(win ? "胜利" : "失败", true,
            win ? (UiTheme.Palette.IsDark ? Color.FromArgb(125, 214, 157) : Color.FromArgb(25, 125, 76))
                : (UiTheme.Palette.IsDark ? Color.FromArgb(245, 151, 158) : Color.FromArgb(188, 64, 75)));
        _champion = new AntdUI.Avatar { Text = "?", Size = new Size(38, 38) };
        _modeTitle = MakeLabel(match.GetModeText(), true);
        _matchInfo = MakeLabel($"{GetChampionName(mine?.championId ?? 0)} · {match.gameCreationDate}", false, UiTheme.Palette.TextSecondary);
        _kda = MakeLabel($"KDA  {mine?.GetKdaText() ?? "—"}", true);
        _duration = MakeLabel(match.GetDurationText(), false, UiTheme.Palette.TextSecondary);
        _details = new AntdUI.Button { Text = "完整详情", Size = new Size(80, 32) };
        _details.Click += (_, _) => MatchDetailForm.OpenAndHandle(_match, _viewerPuuid, this);
        foreach (Control control in new Control[] { _result, _champion, _modeTitle, _matchInfo, _kda, _duration, _details })
            _surface.Controls.Add(control);
        if (mine != null) _ = LoadChampionAsync(_champion, mine.championId);

        int ownTeamId = mine?.teamId ?? 100;
        int otherTeamId = match.participants.FirstOrDefault(player => player.teamId != ownTeamId)?.teamId ?? 200;
        (_blueTeam, _teamOneTitle) = CreateTeam(ownTeamId, "我方", true, _blueRows);
        (_redTeam, _teamTwoTitle) = CreateTeam(otherTeamId, "敌方", false, _redRows);
        _teamHost.Controls.Add(_blueTeam);
        _teamHost.Controls.Add(_redTeam);
        _surface.Controls.Add(_teamHost);
        Resize += (_, _) => LayoutCard();
        LayoutCard();
        _ = DetectPremadesAsync(ownTeamId, otherTeamId);
    }

    private static Label MakeLabel(string text, bool bold = false, Color? color = null) => new()
    {
        Text = text,
        ForeColor = color ?? UiTheme.Palette.TextPrimary,
        Font = new Font("Microsoft YaHei UI", 9F, bold ? FontStyle.Bold : FontStyle.Regular),
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
        BackColor = Color.Transparent
    };

    private (AntdUI.Panel Team, Label Title) CreateTeam(int teamId, string caption,
        bool mine, List<PlayerRow> rows)
    {
        var panel = new AntdUI.Panel
        {
            Tag = teamId,
            Radius = 8,
            BorderWidth = 1,
            BackColor = UiTheme.Palette.SurfaceRaised
        };
        Label title = MakeLabel($"{caption} · {_match.participants.Count(player => player.teamId == teamId)} 人", true,
            mine ? UiTheme.Palette.BlueHeader : UiTheme.Palette.RedHeader);
        panel.Controls.Add(title);
        foreach (MatchParticipant player in _match.participants
            .Where(player => player.teamId == teamId).OrderBy(player => player.participantId))
            rows.Add(CreatePlayerRow(panel, player));

        IReadOnlyList<int> bans = _match.GetBannedChampionIds(teamId);
        if (bans.Count > 0)
        {
            panel.Controls.Add(MakeBanLabel());
            foreach (int championId in bans)
            {
                var tag = new AntdUI.Tag
                {
                    Text = GetChampionName(championId),
                    Size = new Size(90, 25),
                    AutoEllipsis = true,
                    Tag = "ban"
                };
                panel.Controls.Add(tag);
                _ = LoadChampionAsync(tag, championId);
            }
        }
        return (panel, title);
    }

    private static Label MakeBanLabel()
    {
        Label label = MakeLabel("禁用", false, UiTheme.Palette.TextSecondary);
        label.Tag = "ban-label";
        return label;
    }

    private PlayerRow CreatePlayerRow(AntdUI.Panel team, MatchParticipant player)
    {
        MatchPlayer? identity = _match.participantIdentities
            .FirstOrDefault(item => item.participantId == player.participantId)?.player;
        bool isViewer = identity?.Puuid == _viewerPuuid;
        var row = new AntdUI.Panel
        {
            Radius = 6,
            BackColor = isViewer ? UiTheme.Palette.Surface : UiTheme.Palette.SurfaceRaised
        };
        var avatar = new AntdUI.Avatar { Text = "?", Location = new Point(5, 5), Size = new Size(32, 32) };
        row.Controls.Add(avatar);
        _ = LoadChampionAsync(avatar, player.championId);
        string displayName = !string.IsNullOrWhiteSpace(identity?.GameName)
            ? identity.GameName : !string.IsNullOrWhiteSpace(identity?.SummonerName)
                ? identity.SummonerName : $"玩家 {player.participantId}";
        Label name = MakeLabel((isViewer ? "★ " : "") + displayName, true);
        Label score = MakeLabel(player.GetKdaText(), true);
        score.TextAlign = ContentAlignment.MiddleRight;
        Label detail = MakeLabel($"{GetChampionName(player.championId)} · 伤害 {player.stats?.totalDamageDealtToChampions ?? 0:N0}",
            false, UiTheme.Palette.TextSecondary);
        var premade = new AntdUI.Tag
        {
            Text = "开黑",
            Size = new Size(57, 22),
            Visible = false,
            ForeColor = UiTheme.Palette.BlueHeader
        };
        if (!string.IsNullOrWhiteSpace(identity?.Puuid))
            _premadeTags[identity.Puuid] = premade;
        row.Controls.Add(name);
        row.Controls.Add(score);
        row.Controls.Add(detail);
        row.Controls.Add(premade);

        var augments = new List<AntdUI.Tag>();
        if (_match.IsAugmentAram() && player.stats?.AugmentIds.Count > 0)
        {
            foreach (int id in player.stats.AugmentIds)
            {
                var tag = new AntdUI.Tag
                {
                    Text = $"未知强化 #{id}",
                    Size = new Size(108, 23),
                    AutoEllipsis = true,
                    ForeColor = UiTheme.Palette.TextPrimary,
                    BackColor = UiTheme.Palette.SurfaceMuted
                };
                row.Controls.Add(tag);
                augments.Add(tag);
            }
            _ = LoadAugmentsAsync(augments, player.stats.AugmentIds);
        }
        if (!string.IsNullOrWhiteSpace(identity?.Puuid) && !isViewer)
        {
            string puuid = identity.Puuid;
            foreach (Control clickable in new Control[] { row, avatar, name })
            {
                clickable.Cursor = Cursors.Hand;
                clickable.DoubleClick += (_, _) => _ = BattleQueryForm.QueryPlayerAsync(puuid);
            }
        }
        team.Controls.Add(row);
        return new PlayerRow(row, name, score, detail, premade, augments);
    }

    private void LayoutCard()
    {
        if (_layoutBusy || IsDisposed || Width <= 0) return;
        _layoutBusy = true;
        try
        {
            int width = ClientSize.Width;
            _result.SetBounds(14, 15, 48, 38);
            _champion.Location = new Point(66, 18);
            bool narrowHeader = width < 690;
            _details.Location = new Point(Math.Max(244, width - 96), narrowHeader ? 8 : 20);
            if (narrowHeader)
            {
                _modeTitle.SetBounds(114, 3, Math.Max(100, width - 215), 23);
                _matchInfo.SetBounds(114, 25, Math.Max(100, width - 215), 22);
                _kda.SetBounds(114, 50, 120, 23);
                _duration.SetBounds(240, 50, 65, 23);
            }
            else
            {
                _modeTitle.SetBounds(114, 8, Math.Max(95, width - 408), 27);
                _matchInfo.SetBounds(114, 37, Math.Max(95, width - 408), 25);
                _kda.SetBounds(width - 290, 21, 120, 25);
                _duration.SetBounds(width - 163, 21, 65, 25);
            }

            int available = Math.Max(310, width - 24);
            bool stacked = available < 850;
            int teamWidth = stacked ? available : (available - 12) / 2;
            int blueHeight = LayoutTeam(_blueTeam, _teamOneTitle, _blueRows, teamWidth);
            int redHeight = LayoutTeam(_redTeam, _teamTwoTitle, _redRows, teamWidth);
            _teamHost.SetBounds(12, HeaderHeight, available,
                stacked ? blueHeight + redHeight + 10 : Math.Max(blueHeight, redHeight));
            _blueTeam.Location = Point.Empty;
            _redTeam.Location = stacked ? new Point(0, blueHeight + 10) : new Point(teamWidth + 12, 0);
            Height = HeaderHeight + _teamHost.Height + 12;
        }
        finally { _layoutBusy = false; }
    }

    private static int LayoutTeam(AntdUI.Panel team, Label title, IReadOnlyList<PlayerRow> rows, int width)
    {
        title.SetBounds(12, 6, Math.Max(100, width - 24), 27);
        int y = 34;
        foreach (PlayerRow row in rows)
        {
            int rowWidth = Math.Max(280, width - 14);
            int columns = Math.Max(1, (rowWidth - 50) / 113);
            int augmentLines = (row.Augments.Count + columns - 1) / columns;
            int rowHeight = 52 + augmentLines * 27;
            row.Panel.SetBounds(7, y, rowWidth, rowHeight);
            row.Name.SetBounds(44, 2, Math.Max(72, rowWidth - 185), 25);
            row.Premade.Location = new Point(rowWidth - 135, 4);
            row.Score.SetBounds(rowWidth - 76, 3, 69, 25);
            row.Detail.SetBounds(44, 26, Math.Max(80, rowWidth - 52), 22);
            for (int index = 0; index < row.Augments.Count; index++)
                row.Augments[index].Location = new Point(44 + index % columns * 113,
                    51 + index / columns * 27);
            y += rowHeight + 3;
        }

        AntdUI.Tag[] bans = team.Controls.OfType<AntdUI.Tag>()
            .Where(tag => Equals(tag.Tag, "ban")).ToArray();
        if (bans.Length > 0)
        {
            Label? banLabel = team.Controls.OfType<Label>()
                .FirstOrDefault(label => Equals(label.Tag, "ban-label"));
            banLabel?.SetBounds(12, y + 2, 34, 25);
            int columns = Math.Max(1, (width - 55) / 94);
            for (int index = 0; index < bans.Length; index++)
                bans[index].Location = new Point(50 + index % columns * 94,
                    y + index / columns * 28);
            y += (bans.Length + columns - 1) / columns * 28 + 5;
        }
        team.Size = new Size(width, y + 8);
        return team.Height;
    }

    private async Task LoadAugmentsAsync(IReadOnlyList<AntdUI.Tag> tags, IReadOnlyList<int> ids)
    {
        IReadOnlyList<AugmentCatalog.AugmentDisplay> names = await AugmentCatalog.ResolveAsync(ids);
        if (IsDisposed) return;
        for (int index = 0; index < tags.Count && index < names.Count; index++)
        {
            AntdUI.Tag tag = tags[index];
            AugmentCatalog.AugmentDisplay display = names[index];
            tag.Text = display.Name;
            _toolTip.SetToolTip(tag, display.Name);
            if (display.IconUrl != null) _ = LoadAugmentIconAsync(tag, display.IconUrl);
        }
    }

    private async Task DetectPremadesAsync(int ownTeamId, int otherTeamId)
    {
        IReadOnlyList<TeamMemberIdentity> own = TeamIdentities(ownTeamId);
        IReadOnlyList<TeamMemberIdentity> other = TeamIdentities(otherTeamId);
        if (own.Count < 2 || other.Count < 2) return;
        try
        {
            if (PremadeCache.Count > 128) PremadeCache.Clear();
            var cacheKey = (_match.GameId, ownTeamId);
            Task<PremadeDetectionResult> task = _match.GameId > 0
                ? PremadeCache.GetOrAdd(cacheKey, _ => DetectWithLimitAsync(own, other))
                : DetectWithLimitAsync(own, other);
            PremadeDetectionResult result = await task;
            if (IsDisposed) return;
            ApplyPremadeResult(result);
        }
        catch
        {
            if (_match.GameId > 0) PremadeCache.TryRemove((_match.GameId, ownTeamId), out _);
        }
    }

    private async Task<PremadeDetectionResult> DetectWithLimitAsync(
        IReadOnlyList<TeamMemberIdentity> own, IReadOnlyList<TeamMemberIdentity> other)
    {
        await PremadeGate.WaitAsync();
        try { return await _premadeService.DetectAsync(own, other); }
        finally { PremadeGate.Release(); }
    }

    private IReadOnlyList<TeamMemberIdentity> TeamIdentities(int teamId) => _match.participants
        .Where(participant => participant.teamId == teamId)
        .Select(participant => _match.participantIdentities
            .FirstOrDefault(identity => identity.participantId == participant.participantId)?.player)
        .Where(player => !string.IsNullOrWhiteSpace(player?.Puuid))
        .Select(player => new TeamMemberIdentity(player!.Puuid,
            !string.IsNullOrWhiteSpace(player.GameName) ? player.GameName : player.SummonerName))
        .ToArray();

    private void ApplyPremadeResult(PremadeDetectionResult result)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ApplyPremadeResult(result));
            return;
        }
        string ownSummary = result.GetTeamSummary(0);
        string otherSummary = result.GetTeamSummary(1);
        _teamOneTitle.Text = string.IsNullOrEmpty(ownSummary)
            ? $"我方 · {_blueRows.Count} 人" : $"我方 · {_blueRows.Count} 人 · 开黑 {ownSummary}";
        _teamTwoTitle.Text = string.IsNullOrEmpty(otherSummary)
            ? $"敌方 · {_redRows.Count} 人" : $"敌方 · {_redRows.Count} 人 · 开黑 {otherSummary}";
        foreach ((string puuid, AntdUI.Tag tag) in _premadeTags)
        {
            if (!result.GroupByPuuid.TryGetValue(puuid, out PremadeGroup? group)) continue;
            tag.Text = $"开黑{group.Index}";
            tag.Visible = true;
            _toolTip.SetToolTip(tag, $"{string.Join("、", group.Names)}（近期多次同队推断）");
        }
    }

    private static async Task LoadAugmentIconAsync(AntdUI.Tag tag, string url)
    {
        try
        {
            Image? image = Decode(await AugmentCatalog.GetIconAsync(url));
            if (image == null) return;
            if (tag.IsDisposed) { image.Dispose(); return; }
            tag.Image = image;
            tag.Disposed += (_, _) => image.Dispose();
        }
        catch { /* 中文名称仍可显示 */ }
    }

    private async Task LoadChampionAsync(AntdUI.Avatar avatar, int championId)
    {
        try
        {
            Image? image = Decode((await _assets.GetChampionIconAsync(championId))?.Content);
            if (image == null) return;
            if (avatar.IsDisposed) { image.Dispose(); return; }
            avatar.Image = image;
            avatar.Disposed += (_, _) => image.Dispose();
        }
        catch { /* 保留占位头像 */ }
    }

    private async Task LoadChampionAsync(AntdUI.Tag tag, int championId)
    {
        try
        {
            Image? image = Decode((await _assets.GetChampionIconAsync(championId))?.Content);
            if (image == null) return;
            if (tag.IsDisposed) { image.Dispose(); return; }
            tag.Image = image;
            tag.Disposed += (_, _) => image.Dispose();
        }
        catch { /* 保留英雄名称 */ }
    }

    private static Image? Decode(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        try
        {
            using var stream = new MemoryStream(bytes);
            using var source = Image.FromStream(stream);
            return new Bitmap(source);
        }
        catch { return null; }
    }

    private static string GetChampionName(int id) =>
        id > 0 ? AppCompositionRoot.ChampionCatalog.GetDisplayName(id) : "未知英雄";
}