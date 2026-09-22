using LOL_GameAssistant.Application.ChampionSelect;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.LiveGame;
using LOL_GameAssistant.Application.Lobby;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Domain.ChampionSelect;
using LOL_GameAssistant.Domain.Coaching;
using LOL_GameAssistant.Domain.LeagueClient;

namespace LOL_GameAssistant.Application.Coaching;

/// <summary>
/// 编排客户端阶段、选人阵容与当前玩家实时状态，生成发送给 AI 的最小上下文。
/// 不读取认证信息，也不访问隐藏对局数据。
/// </summary>
public sealed class AiGameContextService : IAiGameContextService
{
    private readonly ILobbyService _lobbyService;
    private readonly IPlayerProfileService _playerProfileService;
    private readonly IChampionSelectService _championSelectService;
    private readonly ILiveClientGameStateService _liveClientGameStateService;
    private readonly ILaneKnowledgeService _laneKnowledgeService;
    private readonly IChampionCatalog _championCatalog;
    private string? _cachedMyPuuid;

    public AiGameContextService(
        ILobbyService lobbyService,
        IPlayerProfileService playerProfileService,
        IChampionSelectService championSelectService,
        ILiveClientGameStateService liveClientGameStateService,
        ILaneKnowledgeService laneKnowledgeService,
        IChampionCatalog championCatalog)
    {
        _lobbyService = lobbyService;
        _playerProfileService = playerProfileService;
        _championSelectService = championSelectService;
        _liveClientGameStateService = liveClientGameStateService;
        _laneKnowledgeService = laneKnowledgeService;
        _championCatalog = championCatalog;
    }

    public async Task<AiGameContext> CollectAsync(CancellationToken cancellationToken = default)
    {
        string? phase = await _lobbyService.GetGameFlowPhaseAsync(cancellationToken).ConfigureAwait(false);
        string phaseText = string.IsNullOrWhiteSpace(phase) ? "未连接" : phase;
        string? myPuuid = await GetMyPuuidAsync(cancellationToken).ConfigureAwait(false);

        if (string.Equals(phase, "ChampSelect", StringComparison.OrdinalIgnoreCase))
            return await CollectChampSelectAsync(phaseText, myPuuid, cancellationToken).ConfigureAwait(false);
        if (string.Equals(phase, "InProgress", StringComparison.OrdinalIgnoreCase))
            return await CollectInProgressAsync(phaseText, myPuuid, cancellationToken).ConfigureAwait(false);

        return new AiGameContext
        {
            Phase = phaseText,
            LaneKnowledge = _laneKnowledgeService.GetAdvice("通用", "通用")
        };
    }

    private async Task<AiGameContext> CollectChampSelectAsync(
        string phase,
        string? myPuuid,
        CancellationToken cancellationToken)
    {
        ChampionSelectionSnapshot? session = await _championSelectService.GetSessionAsync(cancellationToken).ConfigureAwait(false);
        if (session == null)
        {
            return new AiGameContext
            {
                Phase = phase,
                Mode = "峡谷选人",
                LaneKnowledge = _laneKnowledgeService.GetAdvice("通用", "通用")
            };
        }

        ChampionSelectionMember? me = session.MyTeam.FirstOrDefault(member => member.CellId == session.LocalPlayerCellId) ??
                                     session.MyTeam.FirstOrDefault(member => member.Puuid == myPuuid);
        // 选人动作会比 myTeam.championId 更早更新；优先读取本人的 pick 动作，
        // 才能在用户刚选定英雄时及时展示 OP.GG 方案，而不是等到锁定之后。
        int selectedPickId = GetCurrentMyActionChampion(session);
        int championId = selectedPickId > 0 ? selectedPickId : me?.ChampionId ?? 0;
        string champion = _championCatalog.GetDisplayName(championId);
        string role = NormalizeRole(me?.AssignedPosition);
        var enemies = session.TheirTeam.Select(member => _championCatalog.GetDisplayName(member.ChampionId)).Where(IsKnownChampion).ToList();
        var allies = session.MyTeam.Select(member => _championCatalog.GetDisplayName(member.ChampionId)).Where(IsKnownChampion).ToList();
        string matchup = enemies.FirstOrDefault() ?? "";

        return new AiGameContext
        {
            Phase = phase,
            Mode = "峡谷选人",
            MyChampion = champion,
            MyChampionId = championId,
            MyRole = role,
            AlliedChampions = allies,
            EnemyChampions = enemies,
            LaneKnowledge = _laneKnowledgeService.GetAdvice(champion, role, matchup)
        };
    }

    private async Task<AiGameContext> CollectInProgressAsync(
        string phase,
        string? myPuuid,
        CancellationToken cancellationToken)
    {
        // LCU 队伍信息与 Live Client 快照相互独立，并行读取以减少一次建议刷新的等待。
        Task<ActiveGameSnapshot?> sessionTask = _lobbyService.GetCurrentSessionAsync(cancellationToken);
        Task<Domain.LiveGame.LiveClientGameSnapshot?> liveSnapshotTask = _liveClientGameStateService.GetSnapshotAsync(cancellationToken);
        ActiveGameSnapshot? session = await sessionTask.ConfigureAwait(false);
        Domain.LiveGame.LiveClientGameSnapshot? liveSnapshot = await liveSnapshotTask.ConfigureAwait(false);
        var teamOne = session?.TeamOne ?? Array.Empty<GameTeamMember>();
        var teamTwo = session?.TeamTwo ?? Array.Empty<GameTeamMember>();
        GameTeamMember? me = teamOne.Concat(teamTwo).FirstOrDefault(member => member.Puuid == myPuuid);
        bool mineIsTeamOne = me != null && teamOne.Any(member => member.Puuid == me.Puuid);
        var allies = (mineIsTeamOne ? teamOne : teamTwo).Select(member => _championCatalog.GetDisplayName(member.ChampionId)).Where(IsKnownChampion).ToList();
        var enemies = (mineIsTeamOne ? teamTwo : teamOne).Select(member => _championCatalog.GetDisplayName(member.ChampionId)).Where(IsKnownChampion).ToList();
        string mode = NormalizeLiveMode(liveSnapshot?.GameMode);
        string champion = _championCatalog.GetDisplayName(me?.ChampionId ?? 0);
        string role = NormalizeRole(me?.Position);
        string matchup = enemies.FirstOrDefault() ?? "";

        return new AiGameContext
        {
            Phase = phase,
            Mode = mode,
            MyChampion = champion,
            MyChampionId = me?.ChampionId ?? 0,
            MyRole = role,
            CurrentGold = liveSnapshot?.CurrentGold ?? 0,
            GameTimeSeconds = liveSnapshot?.GameTimeSeconds ?? 0,
            HasLiveClientData = liveSnapshot != null,
            CurrentItems = liveSnapshot?.Items ?? Array.Empty<string>(),
            AlliedChampions = allies,
            EnemyChampions = enemies,
            LaneKnowledge = _laneKnowledgeService.GetAdvice(champion, role, matchup)
        };
    }

    private static int GetCurrentMyActionChampion(ChampionSelectionSnapshot session) =>
        session.Actions.SelectMany(group => group)
            .FirstOrDefault(action =>
                action.ActorCellId == session.LocalPlayerCellId &&
                string.Equals(action.Type, "pick", StringComparison.OrdinalIgnoreCase) &&
                action.ChampionId > 0)?.ChampionId ?? 0;

    /// <summary>
    /// 实时客户端在未分配位置时会返回字面量 “NONE”，原样送给模型会变成无意义的位置描述
    /// （界面上就会显示“亚索（NONE）”）。这里统一归成“通用”，与选人阶段的兜底一致。
    /// </summary>
    private static string NormalizeRole(string? position)
    {
        string value = (position ?? "").Trim();
        return value.Length == 0 ||
               value.Equals("NONE", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("UNSELECTED", StringComparison.OrdinalIgnoreCase)
            ? "通用"
            : value;
    }

    private async Task<string?> GetMyPuuidAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_cachedMyPuuid)) return _cachedMyPuuid;
        try
        {
            _cachedMyPuuid = (await _playerProfileService.GetCurrentAsync(cancellationToken).ConfigureAwait(false))?.Puuid;
        }
        catch
        {
            // 本地资料端点短暂不可用时保留空身份；AI 时间线会等待下一次完整上下文刷新。
        }
        return _cachedMyPuuid;
    }

    private static bool IsKnownChampion(string name) => !string.IsNullOrWhiteSpace(name);

    private static string NormalizeLiveMode(string? mode) => mode?.Trim().ToUpperInvariant() switch
    {
        "ARAM" => "深渊大乱斗",
        "CLASSIC" or "CLASSIC SR" => "峡谷对局",
        _ => "峡谷对局"
    };
}
