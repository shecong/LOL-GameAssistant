using LOL_GameAssistant.Application.ApplicationInfo;
using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Application.ChampionSelect;
using LOL_GameAssistant.Application.ClientFeatures;
using LOL_GameAssistant.Application.Coaching;
using LOL_GameAssistant.Application.Files;
using LOL_GameAssistant.Application.Friends;
using LOL_GameAssistant.Application.GameData;
using LOL_GameAssistant.Application.Insights;
using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.LiveGame;
using LOL_GameAssistant.Application.Lobby;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Application.Players;
using LOL_GameAssistant.Application.Profiles;
using LOL_GameAssistant.Application.Ranked;
using LOL_GameAssistant.Application.Settings;
using LOL_GameAssistant.Application.Skins;
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
    static AppCompositionRoot()
    {
        System.Windows.Forms.Application.ApplicationExit += (_, _) => TclsLoginLauncher.Cancel();
        System.Windows.Forms.Application.ApplicationExit += (_, _) => RankBorderLoading.Dispose();
        RankBorderLoading.Changed += update => LOL_GameAssistant.Helper.RuntimeDiagnostics.Report("加载边框", update.Status, update.Message);
        MatchAnalysisSettings.Policy = () =>
        {
            var settings = ApplicationSettingsStore.Load();
            return new(50, settings.RecentPerformanceMinimumGames);
        };
        PlayerHistoryRepository.For(MatchHistoryService).Diagnostic += detail =>
            LOL_GameAssistant.Helper.RuntimeDiagnostics.Report("战绩共享仓库", "读取", detail);
        MatchHistoryRequestPolicy.Shared.Diagnostic = detail =>
            LOL_GameAssistant.Helper.RuntimeDiagnostics.Report("战绩请求队列", "读取", detail);
        Automation.Changed += step => LOL_GameAssistant.Helper.RuntimeDiagnostics.Report("自动化 · " + step.Name, step.State, step.Detail);
        if (LcuRequestSender is LcuHttpRequestSender requests) requests.RequestCompleted += ClientCapabilities.Observe;
    }
    public static LOL_GameAssistant.Application.Skins.ISkinCoreService SkinCoreService { get; } =
        new LOL_GameAssistant.Infrastructure.Skins.NativeSkinCoreService(
            new LOL_GameAssistant.Infrastructure.Skins.SelectableSkinCoreTransport(
                new LOL_GameAssistant.Infrastructure.Skins.NativeSkinCoreTransport(Path.Combine(AppContext.BaseDirectory, "Tools", "SkinCore"),
                    diagnostic: LOL_GameAssistant.Helper.RuntimeDiagnostics.ReportSkinCoreFailure),
                new LOL_GameAssistant.Infrastructure.Skins.NativeSkinCoreTransport(Path.Combine(AppContext.BaseDirectory, "Tools", "SkinCoreHybrid"), kernelRelay: true,
                    diagnostic: LOL_GameAssistant.Helper.RuntimeDiagnostics.ReportSkinCoreFailure),
                new LegacyApplicationSettingsStore().Load().SkinCoreMode),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LOL-GameAssistant", "SkinCore", "events.jsonl"));
    private static readonly ILcuRequestSender LcuRequestSender = new LcuHttpRequestSender();
    public static IClientRegaliaService ClientRegaliaService { get; } = new LcuClientRegaliaService(LcuRequestSender);
    public static IClientIdentityService ClientIdentityService { get; } = new LcuClientIdentityService(LcuRequestSender,
        detail => LOL_GameAssistant.Helper.RuntimeDiagnostics.Report("身份装饰", "诊断", detail));
    private static readonly Infrastructure.Sgp.SgpClient Sgp = new(LcuRequestSender);
    private static readonly PlayerDataCacheScope PlayerCacheScope = new(LcuRequestSender);
    public static IClientCapabilityService ClientCapabilities { get; } = new LcuClientCapabilityService(LcuRequestSender);
    public static IPartyService PartyService { get; } = new LcuPartyService(LcuRequestSender);

    public static LOL_GameAssistant.Application.Skins.IChampionSkinResourceService ChampionSkinResourceService { get; } =
        new LcuChampionSkinResourceService(LcuRequestSender, () => LOL_GameAssistant.LoLApi.Game_Api.gameversion);

    public static Infrastructure.Lounge.LoungeClient CreateLoungeClient() =>
        new(new Infrastructure.Lounge.LoungeLcuReader(LcuRequestSender, MatchHistoryService, RankedStatsService));

    public static LcuQuickShoutService QuickShoutService { get; } = new(LcuRequestSender);

    /// <summary>本地客户端扫描与启动服务。</summary>
    public static IGameClientLauncher GameClientLauncher => TclsLoginLauncher;

    /// <summary>发布版本查询服务。</summary>
    public static IUpdateReleaseService UpdateReleaseService { get; } = new ServerUpdateReleaseService();

    /// <summary>用户明确选择的文本导出服务。</summary>
    public static ITextExportService TextExportService { get; } = new TextExportService();
    public static ICompanionExperienceStore CompanionExperienceStore { get; } = new CompanionExperienceStore();
    public static IPatchWatchService PatchWatchService { get; } = new DataDragonPatchWatchService();

    private static readonly LeagueClientConnection LeagueClientConnectionImplementation = new();

    /// <summary>LCU 连接探测服务。</summary>
    public static ILeagueClientConnection LeagueClientConnection { get; } = LeagueClientConnectionImplementation;

    /// <summary>LCU 游戏流程事件流；认证和 WebSocket 协议由基础设施处理。</summary>
    public static ILeagueClientEventStream LeagueClientEventStream { get; } =
        new LcuGameFlowEventStream(LeagueClientConnectionImplementation);

    /// <summary>好友观战用例的应用服务实例。</summary>
    public static IFriendSpectateService FriendSpectateService { get; } =
        new LcuFriendSpectateService(LcuRequestSender);

    public static IPlayerSpectateService PlayerSpectateService { get; } =
        new LcuPlayerSpectateService((ILcuResultRequestSender)LcuRequestSender);

    /// <summary>好友列表查询服务。</summary>
    public static IFriendDirectoryService FriendDirectoryService { get; } =
        new LcuFriendDirectoryService(LcuRequestSender);

    /// <summary>玩家头像资源服务。</summary>
    public static IProfileIconService ProfileIconService { get; } =
        new LcuProfileIconService(LcuRequestSender);

    /// <summary>召唤师资料查询服务。</summary>
    public static IPlayerProfileService PlayerProfileService { get; } =
        new CachedPlayerProfileService(new Infrastructure.Sgp.SgpPlayerProfileService(Sgp, new LcuPlayerProfileService(LcuRequestSender)), context: PlayerCacheScope.GetAsync);

    /// <summary>收藏召唤师列表存储。</summary>
    public static IFavoritePlayerStore FavoritePlayerStore { get; } = new LegacyFavoritePlayerStore();

    /// <summary>助手设置存储与 API Key 保护服务。</summary>
    public static IApplicationSettingsStore ApplicationSettingsStore { get; } = new LegacyApplicationSettingsStore();

    public static ISettingsSecretProtector SettingsSecretProtector { get; } = new DpapiSettingsSecretProtector();
    public static RankBorderLoadingCoordinator RankBorderLoading { get; } = new(SkinCoreService,
        () => ApplicationSettingsStore.Load(), () =>
        {
            var processes = System.Diagnostics.Process.GetProcessesByName("League of Legends");
            try { return processes.Length == 1 ? processes[0].Id : null; }
            finally { foreach (var process in processes) process.Dispose(); }
        });
    public static ITclsAccountStore TclsAccounts { get; } = new LocalTclsAccountStore();
    public static TclsAutomaticClientLauncher TclsLoginLauncher { get; } = new(new LocalGameClientLauncher(),
        () => ApplicationSettingsStore.Load(), TclsAccounts);

    /// <summary>战绩列表和单局详情查询服务。</summary>
    public static IMatchHistoryService MatchHistoryService { get; } = new CachedMatchHistoryService(new Infrastructure.Sgp.SgpMatchHistoryService(Sgp), context: PlayerCacheScope.GetAsync);

    public static EnemyJungleRouteService CreateEnemyJungleRouteService() =>
        new(LobbyService, PlayerProfileService, MatchHistoryService, new Infrastructure.Sgp.SgpMatchTimelineService(Sgp));

    /// <summary>单双排、灵活组排查询服务。</summary>
    public static IRankedStatsService RankedStatsService { get; } = new CachedRankedStatsService(new Infrastructure.Sgp.SgpRankedStatsService(Sgp), context: PlayerCacheScope.GetAsync);

    /// <summary>游戏数据版本初始化服务。</summary>
    public static IGameDataVersionService GameDataVersionService { get; } = new LegacyGameDataVersionService();

    /// <summary>英雄、装备和技能等只读游戏资源服务。</summary>
    public static IGameAssetService GameAssetService { get; } = new CachedGameAssetService(
        new LegacyGameAssetService(), () => LOL_GameAssistant.LoLApi.Game_Api.gameversion);

    public static IGameOverlayBridge GameOverlay { get; } = new NativeGameOverlayBridge();
    private static readonly AugmentReviewScanner ReviewedAugments = new(new LocalAugmentScanner(GameOverlay),
        () => AugmentCatalog.GetAll().Where(c => c.Id >= 1000).Select(c => new AugmentCatalogChoice(c.Id, c.Name, AugmentCatalog.GetDescription(c.Id))).ToArray());
    public static IAugmentScanner AugmentScanner { get; } = ReviewedAugments;
    public static IAugmentReviewService AugmentReview { get; } = ReviewedAugments;
    public static IAugmentInfoService AugmentInfoService { get; } = new AugmentInfoService();

    /// <summary>英雄 ID 与展示名称目录。</summary>
    public static IChampionCatalog ChampionCatalog { get; } = new LegacyChampionCatalog();

    /// <summary>外置版 OP.GG 英雄 T 级与 ARAM 平衡修正公开数据。</summary>
    public static IChampionInsightsService ChampionInsightsService { get; } =
        new OpggChampionInsightsService(ChampionCatalog);

    /// <summary>大厅、匹配确认与游戏流程服务。</summary>
    public static ILobbyService LobbyService { get; } = new LegacyLobbyService();

    internal static RankedLpTracker RankedLpTracker { get; } = new(LobbyService,
        new LcuPlayerProfileService(LcuRequestSender), new LegacyRankedStatsService(),
        new Infrastructure.Sgp.SgpMatchHistoryService(Sgp), RankedLpStore.Shared);

    /// <summary>选人读取和自动禁用、选用服务。</summary>
    public static IChampionSelectService ChampionSelectService { get; } = new LegacyChampionSelectService();

    /// <summary>向 LCU 当前英雄选择聊天会话发送一条消息。</summary>
    public static IChampionSelectChatService ChampionSelectChatService { get; } =
        new LcuChampionSelectChatService(LcuRequestSender);

    public static ICurrentRegionService CurrentRegionService { get; } = new LcuCurrentRegionService(LcuRequestSender);

    /// <summary>回放、奖励、赛后、社交与快捷大厅等外置客户端增强功能。</summary>
    public static IClientFeatureService ClientFeatureService { get; } =
        new LcuClientFeatureService(LcuRequestSender, ClientCapabilities);

    /// <summary>OP.GG 公开推荐到本机 LCU 符文页和自定义物品集的一键配置。</summary>
    public static IOpggBuildApplyService OpggBuildApplyService { get; } =
        new VerifiedBuildApplyService(new CachedOpggBuildApplyService(new OpggBuildApplyService(LcuRequestSender, ChampionCatalog, RememberAppliedRune),
            () => LOL_GameAssistant.LoLApi.Game_Api.gameversion), LcuRequestSender);

    private static void RememberAppliedRune(LOL_GameAssistant.Domain.Builds.PersonalRunePreset applied)
    {
        var settings = ApplicationSettingsStore.Load();
        var existing = settings.PersonalRunePresets.FirstOrDefault(preset => preset.ChampionId == applied.ChampionId &&
            preset.Mode == applied.Mode && preset.Position == applied.Position && preset.PrimaryStyleId == applied.PrimaryStyleId &&
            preset.SubStyleId == applied.SubStyleId && preset.RunePerkIds.SequenceEqual(applied.RunePerkIds));
        if (existing == null) settings.PersonalRunePresets.Add(applied);
        else { existing.LastAppliedAt = applied.LastAppliedAt; existing.ItemBlocks = applied.ItemBlocks; }
        ApplicationSettingsStore.Save(settings);
    }

    /// <summary>仅读取本机当前玩家实时状态的服务。</summary>
    public static ILiveClientGameStateService LiveClientGameStateService { get; } = new LiveClientGameStateService();

    public static LOL_GameAssistant.Application.LiveGame.BattleOverlayService BattleOverlayService { get; } =
        new(PlayerProfileService, RankedStatsService, MatchHistoryService);

    public static ILocalCacheManagementService LocalCacheManagementService { get; } = new LocalCacheManagementService(matchCaches:
        [(BaseViewForm.LivePlayerForm.MeasureSharedCaches, BaseViewForm.LivePlayerForm.ClearSharedCaches),
         (BaseViewForm.MatchHistoryCard.MeasureSharedCache, BaseViewForm.MatchHistoryCard.ClearSharedCache),
         (PlayerHistoryRepository.For(MatchHistoryService).Measure, PlayerHistoryRepository.For(MatchHistoryService).Clear)]);

    private static readonly ILaneKnowledgeService LaneKnowledgeService = new FileLaneKnowledgeService();

    private static readonly IAiGameContextService AiGameContextService = new AiGameContextService(
        LobbyService,
        PlayerProfileService,
        ChampionSelectService,
        LiveClientGameStateService,
        LaneKnowledgeService,
        ChampionCatalog, AugmentReview);

    /// <summary>近期同队关系的开黑检测服务。</summary>
    public static IPremadeDetectionService PremadeDetectionService { get; } = new LegacyPremadeDetectionService(MatchHistoryService);
    public static IRegionalPlayerQueryFactory RegionalPlayerQueries { get; } = new Infrastructure.Sgp.SgpRegionalPlayerQueryFactory(
        LcuRequestSender, new(PlayerProfileService, MatchHistoryService, RankedStatsService, PremadeDetectionService));

    /// <summary>本机上下文与云端 AI 建议服务。</summary>
    public static IAiCoachingService AiCoachingService { get; } = new LegacyAiCoachingService(AiGameContextService);

    /// <summary>云端 AI 共用的 HTTP 客户端：单实例复用连接，避免每次建议都重新握手。</summary>
    // 禁止重定向，避免自定义服务把带 API Key 的请求转发到另一个地址。
    private static readonly HttpClient AiHttpClient = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(25)
    };

    /// <summary>根据当前可见对局数据生成时间线建议的 AI 适配器。</summary>
    public static IAiRecommendationProvider AiRecommendationProvider { get; } =
        new CloudAiRecommendationProvider(new CloudAiRecommendationService(AiHttpClient));

    /// <summary>独立于页面生命周期的局内建议调度器。</summary>
    public static IRecommendationCoordinator RecommendationCoordinator { get; } =
        new RecommendationCoordinator(AiCoachingService, AiRecommendationProvider, laneKnowledge: LaneKnowledgeService);
    public static IPlayerGrowthService PlayerGrowthService { get; } = new LcuPlayerGrowthService(MatchHistoryService, ChampionCatalog, LcuRequestSender);
    public static Application.Automation.PostGameAutomation Automation { get; } = new(ClientFeatureService, PartyService);
}
