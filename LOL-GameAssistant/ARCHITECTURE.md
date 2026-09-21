# LOL GameAssistant 领域架构约定

项目按 DDD（领域驱动设计）逐步收敛为以下四层：

```text
Presentation（BaseViewForm / GameMain）
        ↓ 仅调用应用接口
Application（用例、端口、编排）
        ↓ 只依赖
Domain（规则、值对象、领域结果）
        ↑ 由基础设施实现端口
Infrastructure（LCU、文件、HTTP、Windows 进程）
Bootstrap（唯一组合根，装配接口与实现）
```

## 依赖规则

- `Domain` 不能引用 WinForms、LCU、HTTP、文件系统或配置类。
- `Application` 声明用例和端口接口；不能直接创建 `HttpClient`、读取 LCU token 或操作控件。
- `Infrastructure` 实现应用端口，集中处理 LCU、目录扫描、系统 API 与外部网络。
- `Presentation` 只收集输入、展示结果和调用应用服务；不得拼接 LCU 路径或持有认证信息。
- `Bootstrap/AppCompositionRoot.cs` 是装配基础设施实现的唯一入口。

## 已迁移的关键用例

| 用例 | 领域 | 应用 | 基础设施 |
| --- | --- | --- | --- |
| 客户端启动、连接与事件 | `Domain/LeagueClient` | `IGameClientLauncher`、`ILeagueClientConnection`、`ILeagueClientEventStream` | `LocalGameClientLauncher`、`LeagueClientConnection`、`LcuGameFlowEventStream` |
| 好友列表与观战 | `Domain/Friends` | `IFriendDirectoryService`、`IFriendSpectateService`、`IProfileIconService`、`ILcuRequestSender` | `LcuFriendDirectoryService`、`LcuFriendSpectateService`、`LcuProfileIconService`、`LcuHttpRequestSender` |
| 赛后表现 | `Domain/MatchAnalysis` | 由表现层适配已结束对局数据 | 无外部依赖 |
| 召唤师资料 | `Domain/Players` | `IPlayerProfileService` | `LcuPlayerProfileService` |
| 战绩与排位 | `Domain/Matches`、`Domain/MatchAnalysis`、`Domain/Ranked` | `IMatchHistoryService`、`IRankedStatsService` | `LegacyMatchHistoryService`、`LegacyMatchReadModelMapper`、`LegacyRankedStatsService` |
| 游戏资源 | `Domain/GameData` | `IGameDataVersionService`、`IGameAssetService` | `LegacyGameDataVersionService`、`LegacyGameAssetService` |
| 英雄目录 | `Domain/GameData` | `IChampionCatalog` | `LegacyChampionCatalog` |
| 大厅与选人 | `Domain/LeagueClient`、`Domain/ChampionSelect` | `ILobbyService`、`IChampionSelectService` | `LegacyLobbyService`、`LegacyChampionSelectService` |
| 实时当前玩家 | `Domain/LiveGame` | `ILiveClientGameStateService` | `LiveClientGameStateService` |
| 开黑关系推断 | `Domain/Teams` | `IPremadeDetectionService` | `LegacyPremadeDetectionService` |
| AI 训练建议 | `Domain/Coaching` | `IAiCoachingService` | `LegacyAiCoachingService` |
| 本地设置与收藏 | `Domain/Settings` | `IApplicationSettingsStore`、`ISettingsSecretProtector`、`IFavoritePlayerStore` | `LegacyApplicationSettingsStore`、`LegacySettingsMapper`、`DpapiSettingsSecretProtector`、`LegacyFavoritePlayerStore` |
| 应用更新 | `Domain/ApplicationInfo` | `IUpdateReleaseService` | `GitHubUpdateReleaseService` |
| 用户导出 | 无额外业务规则 | `ITextExportService` | `TextExportService` |

## 旧 LCU DTO 的边界

`Entity` 目录中的 `GameHeadModel`、`GameDetailModel`、`LolRankedDataParser`、`ChampSelectSession` 等类型
只作为旧 LCU / JSON 反序列化 DTO 使用，允许出现于 `LoLApi` 与 `Infrastructure`，禁止出现在
`Domain`、`Application`、`BaseViewForm` 和 `GameMain`。基础设施必须先映射为
`Domain/Matches`、`Domain/Ranked`、`Domain/LeagueClient` 或 `Domain/ChampionSelect` 的稳定读模型。

WinForms 不得调用 `Game_Api`、`Assets_api`、`Select_Api`、`GetlolLcu` 或 `HttpClentHelper`，
也不得直接读写 `settings.json` / `favorites.json`、调用 DPAPI、解析网络响应或读取英雄静态表；
这些工作必须经 `Application` 端口进入 `Infrastructure`。迁移不得改写已有保存字段、LCU 端点或用户界面行为。

## 旧模块迁移顺序

1. 将 `LoLApi` 中的静态 LCU 调用拆为 `Infrastructure/LeagueClient` 适配器。
2. 将战绩、好友、对局建议等业务流程抽到 `Application` 用例。
3. 将 `Entity` 中属于业务规则的类型迁到 `Domain`；仅保留 LCU/JSON DTO 在基础设施层。
4. 将 `BaseViewForm` 重命名并迁入 `Presentation`，通过构造函数接收应用接口。

迁移过程中保留旧配置 JSON 字段的兼容读取，不在未经用户确认的情况下修改已保存的行为。
