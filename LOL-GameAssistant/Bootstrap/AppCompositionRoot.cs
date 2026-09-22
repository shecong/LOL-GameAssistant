using LOL_GameAssistant.Application.ApplicationInfo;
using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.ChampionSelect;
using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Application.Files;
using LOL_GameAssistant.Application.Friends;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.LiveGame;
using LOL_GameAssistant.Application.Lobby;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Application.Teams;
using LOL_GameAssistant.Infrastructure.Ai;
using LOL_GameAssistant.Infrastructure.ApplicationInfo;
using LOL_GameAssistant.Infrastructure.Coaching;
using LOL_GameAssistant.Infrastructure.Files;
using LOL_GameAssistant.Infrastructure.GameData;
using LOL_GameAssistant.Infrastructure.LeagueClient;
using LOL_GameAssistant.Infrastructure.LiveGame;
using LOL_GameAssistant.Infrastructure.Players;
using LOL_GameAssistant.Infrastructure.Settings;

namespace LOL_GameAssistant.Bootstrap;

/// <summary>
/// 应用组合根：仅此处允许把应用接口与基础设施实现装配在一起。
/// 领域、应用和表现层均不直接 new LCU 传输对象。
/// </summary>
public static class AppCompositionRoot
{
    private static readonly ILcuRequestSender LcuRequestSender = new LcuHttpRequestSender();

    /// <summary>本地客户端扫描与启动服务。</summary>
    public static IGameClientLauncher GameClientLauncher { get; } = new LocalGameClientLauncher();

    /// <summary>发布版本查询服务。</summary>
    public static IUpdateReleaseService UpdateReleaseService { get; } = new GitHubUpdateReleaseService();

    /// <summary>用户明确选择的文本导出服务。</summary>
    public static ITextExportService TextExportService { get; } = new TextExportService();

    private static readonly LeagueClientConnection LeagueClientConnectionImplementation = new();

    /// <summary>LCU 连接探测服务。</summary>
    public static ILeagueClientConnection LeagueClientConnection { get; } = LeagueClientConnectionImplementation;

    /// <summary>LCU 游戏流程事件流；认证和 WebSocket 协议由基础设施处理。</summary>
    public static ILeagueClientEventStream LeagueClientEventStream { get; } =
        new LcuGameFlowEventStream(LeagueClientConnectionImplementation);

    /// <summary>好友观战用例的应用服务实例。</summary>
    public static IFriendSpectateService FriendSpectateService { get; } =
        new LcuFriendSpectateService(LcuRequestSender);

    /// <summary>好友列表查询服务。</summary>
    public static IFriendDirectoryService FriendDirectoryService { get; } =
        new LcuFriendDirectoryService(LcuRequestSender);

    /// <summary>玩家头像资源服务。</summary>
    public static IProfileIconService ProfileIconService { get; } =
        new LcuProfileIconService(LcuRequestSender);

    /// <summary>召唤师资料查询服务。</summary>
    public static IPlayerProfileService PlayerProfileService { get; } =
        new LcuPlayerProfileService(LcuRequestSender);

    /// <summary>收藏召唤师列表存储。</summary>
    public static IFavoritePlayerStore FavoritePlayerStore { get; } = new LegacyFavoritePlayerStore();

    /// <summary>助手设置存储与 API Key 保护服务。</summary>
    public static IApplicationSettingsStore ApplicationSettingsStore { get; } = new LegacyApplicationSettingsStore();

    public static ISettingsSecretProtector SettingsSecretProtector { get; } = new DpapiSettingsSecretProtector();

    /// <summary>战绩列表和单局详情查询服务。</summary>
    public static IMatchHistoryService MatchHistoryService { get; } = new LegacyMatchHistoryService();

    /// <summary>单双排、灵活组排查询服务。</summary>
    public static IRankedStatsService RankedStatsService { get; } = new LegacyRankedStatsService();

    /// <summary>游戏数据版本初始化服务。</summary>
    public static IGameDataVersionService GameDataVersionService { get; } = new LegacyGameDataVersionService();

    /// <summary>英雄、装备和技能等只读游戏资源服务。</summary>
    public static IGameAssetService GameAssetService { get; } = new LegacyGameAssetService();

    /// <summary>英雄 ID 与展示名称目录。</summary>
    public static IChampionCatalog ChampionCatalog { get; } = new LegacyChampionCatalog();

    /// <summary>大厅、匹配确认与游戏流程服务。</summary>
    public static ILobbyService LobbyService { get; } = new LegacyLobbyService();

    /// <summary>选人读取和自动禁用、选用服务。</summary>
    public static IChampionSelectService ChampionSelectService { get; } = new LegacyChampionSelectService();

    /// <summary>向 LCU 当前英雄选择聊天会话发送一条消息。</summary>
    public static IChampionSelectChatService ChampionSelectChatService { get; } =
        new LcuChampionSelectChatService(LcuRequestSender);

    /// <summary>OP.GG 公开推荐到本机 LCU 符文页和自定义物品集的一键配置。</summary>
    public static IOpggBuildApplyService OpggBuildApplyService { get; } =
        new OpggBuildApplyService(LcuRequestSender, ChampionCatalog);

    /// <summary>仅读取本机当前玩家实时状态的服务。</summary>
    public static ILiveClientGameStateService LiveClientGameStateService { get; } = new LiveClientGameStateService();

    private static readonly ILaneKnowledgeService LaneKnowledgeService = new FileLaneKnowledgeService();

    private static readonly IAiGameContextService AiGameContextService = new AiGameContextService(
        LobbyService,
        PlayerProfileService,
        ChampionSelectService,
        LiveClientGameStateService,
        LaneKnowledgeService,
        ChampionCatalog);

    /// <summary>近期同队关系的开黑检测服务。</summary>
    public static IPremadeDetectionService PremadeDetectionService { get; } = new LegacyPremadeDetectionService();

    /// <summary>本机上下文与云端 AI 建议服务。</summary>
    public static IAiCoachingService AiCoachingService { get; } = new LegacyAiCoachingService(AiGameContextService);

    /// <summary>云端 AI 共用的 HTTP 客户端：单实例复用连接，避免每次建议都重新握手。</summary>
    private static readonly HttpClient AiHttpClient = new() { Timeout = TimeSpan.FromSeconds(25) };

    /// <summary>根据当前可见对局数据生成时间线建议的 AI 适配器。</summary>
    public static IAiRecommendationProvider AiRecommendationProvider { get; } =
        new CloudAiRecommendationProvider(new CloudAiRecommendationService(AiHttpClient));

    /// <summary>独立于页面生命周期的局内建议调度器。</summary>
    public static IRecommendationCoordinator RecommendationCoordinator { get; } =
        new RecommendationCoordinator(AiCoachingService, AiRecommendationProvider);
}
