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
        string? myPuuid = (await _playerProfileService.GetCurrentAsync(cancellationToken).ConfigureAwait(false))?.Puuid;

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
        int championId = me?.ChampionId ?? GetCurrentMyActionChampion(session);
        string champion = _championCatalog.GetDisplayName(championId);
        string role = string.IsNullOrWhiteSpace(me?.AssignedPosition) ? "通用" : me.AssignedPosition;
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
        ActiveGameSnapshot? session = await _lobbyService.GetCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
        var teamOne = session?.TeamOne ?? Array.Empty<GameTeamMember>();
        var teamTwo = session?.TeamTwo ?? Array.Empty<GameTeamMember>();
        GameTeamMember? me = teamOne.Concat(teamTwo).FirstOrDefault(member => member.Puuid == myPuuid);
        bool mineIsTeamOne = me != null && teamOne.Any(member => member.Puuid == me.Puuid);
        var allies = (mineIsTeamOne ? teamOne : teamTwo).Select(member => _championCatalog.GetDisplayName(member.ChampionId)).Where(IsKnownChampion).ToList();
        var enemies = (mineIsTeamOne ? teamTwo : teamOne).Select(member => _championCatalog.GetDisplayName(member.ChampionId)).Where(IsKnownChampion).ToList();
        var ownState = await _liveClientGameStateService.GetOwnStateAsync(cancellationToken).ConfigureAwait(false);
        string? rawMode = await _liveClientGameStateService.GetGameModeAsync(cancellationToken).ConfigureAwait(false);
        string mode = NormalizeLiveMode(rawMode);
        string champion = _championCatalog.GetDisplayName(me?.ChampionId ?? 0);
        string role = string.IsNullOrWhiteSpace(me?.Position) ? "通用" : me!.Position;
        string matchup = enemies.FirstOrDefault() ?? "";

        return new AiGameContext
        {
            Phase = phase,
            Mode = mode,
            MyChampion = champion,
            MyChampionId = me?.ChampionId ?? 0,
            MyRole = role,
            CurrentGold = ownState?.CurrentGold ?? 0,
            GameTimeSeconds = ownState?.GameTimeSeconds ?? 0,
            CurrentItems = ownState?.Items ?? Array.Empty<string>(),
            AlliedChampions = allies,
            EnemyChampions = enemies,
            LaneKnowledge = _laneKnowledgeService.GetAdvice(champion, role, matchup)
        };
    }

    private static int GetCurrentMyActionChampion(ChampionSelectionSnapshot session) =>
        session.Actions.SelectMany(group => group)
            .FirstOrDefault(action => action.ActorCellId == session.LocalPlayerCellId && action.ChampionId > 0)?.ChampionId ?? 0;

    private static bool IsKnownChampion(string name) => !string.IsNullOrWhiteSpace(name);

    private static string NormalizeLiveMode(string? mode) => mode?.Trim().ToUpperInvariant() switch
    {
        "ARAM" => "深渊大乱斗",
        "CLASSIC" or "CLASSIC SR" => "峡谷对局",
        _ => "峡谷对局"
    };
}
