# API 与事件流可靠性改进

本次处理审计 IT-03（事件顺序）、IT-04（资源容量）与 IT-05（API 故障恢复）。这是源码整改记录，真实 League 客户端和公网部署仍需验收，不替代原审计的环境边界。

## API

DataDragon 使用一个共享刷新任务，所有并发调用等待同一次请求。调用方取消只结束自己的等待，不会取消其他调用方共用的刷新。上游失败时返回已有版本；没有缓存时保持既有 `VERSION_UNAVAILABLE` JSON 协议，版本接口仍返回 HTTP 200。

`LOL-GameApi/appsettings.json` 中的 `DataDragon` 配置：

| 配置 | 默认值 | 行为 |
| --- | --- | --- |
| CacheDuration | 6 小时 | 成功数据的缓存期限 |
| RequestTimeout | 5 秒 | 覆盖连接、响应头和响应体读取 |
| FailureCooldown | 30 秒 | 第一次失败后的冷却时间 |
| MaxFailureCooldown | 5 分钟 | 连续失败指数退避的上限 |
| MaxResponseBytes | 64 KiB | 同时检查声明长度及实际读取字节数 |

成功刷新会重置失败计数。网络故障、超时、无效 JSON、无效版本和超大响应均进入冷却，并记录服务日志；缓存时钟改用 UTC。这里保留最后一次成功版本作为降级数据，没有设置旧缓存最大存活期。

版本接口与 readiness 共用全局 32 个并发许可，不排队，超限返回 HTTP 429。该限制适用于单进程；多实例和公网流量仍需要网关层配合。

- `/api/health`：原有进程存活检查，行为不变。
- `/api/health/ready`：共用版本刷新；有可用缓存返回 200，无缓存且上游不可用返回 503；响应包含 `degraded`、最近成功/失败、下次重试和连续失败数。冷启动的 readiness 会触发一次有时限的刷新，避免无缓存时永远无法进入就绪状态。

请求取消不再被全局异常处理标为 HTTP 500；响应已经开始时不会再次拼接错误 JSON。

## WebSocket

每个连接只运行一个消息接收与业务事件消费者。上一事件完成后才读取下一条，使用网络背压，没有无界消息任务队列。慢消费者会延迟后续消息，这是容量与顺序保证的明确取舍。

默认单条消息最多 **1 MiB**，首次未完成分片到消息完成最多 **10 秒**，单次连接最多 **10 秒**。空闲连接不触发分片超时。超大消息、分片超时及非文本消息会结束当前连接，并按已有策略重连。

接收、心跳和重连均捕获各自连接实例及取消令牌；关闭连接会取消尚未完成的异步消费者。异步事件从 WebSocket、LCU 解析一直等待至 UI 处理完成，避免线程池任务反序。心跳回复先于业务分发；只有 `127.0.0.1` 保留本机自签名证书例外。

阶段快照查询记录状态版本；若查询期间出现新阶段或连接变化，旧结果不再覆盖当前阶段。手动刷新对局页使用相同版本校验。

`ILeagueClientEventStream.EventReceived` 的订阅类型改为 `Func<LeagueClientEvent, CancellationToken, Task>`；订阅者必须返回实际处理任务，并遵守连接取消令牌。同步 `WebSocketClient.OnMessage` 保留，但订阅者自行创建后台任务时，封装无法保证那些任务内部的完成顺序。

## 验证

新增故障回归覆盖：并发请求合并与独立取消、冷却上限和恢复、过期缓存与 readiness、响应头/响应体超时、异常数据及未知长度超大响应；WebSocket 覆盖慢消费者背压、顺序、取消、UTF-8 分片、消息上限、分片超时、心跳，以及真实本机 Kestrel WebSocket 的关闭、再次连接和自动重连。

```powershell
dotnet test LOL-GameApi.Tests/LOL-GameApi.Tests.csproj
dotnet test LOL-GameAssistant.Tests/LOL-GameAssistant.Tests.csproj
dotnet test LOL-GameAssistant.UiTests/LOL-GameAssistant.UiTests.csproj
dotnet build LOL-GameAssistant.slnx -c Release --no-restore --no-incremental
```

API 测试项目已加入解决方案。测试不访问真实 League 客户端、玩家聊天或付费 AI；上游故障使用模拟 HTTP，WebSocket 生命周期使用本机临时端口。

验证结果：API 与本机连接测试 12 项、单元测试 155 项、Windows 界面测试 101 项，共 268 项通过；Release 非增量构建 0 警告、0 错误。实际启动 API 的本机冒烟检查确认 liveness、readiness 与版本接口返回 200，并成功读取 DataDragon。TRX 保存在 `artifacts/resilience`（Git 忽略目录）。未执行真实 League 客户端端到端测试或公网压力测试。
