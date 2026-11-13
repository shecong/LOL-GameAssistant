# 客户端视觉插件 v0.2.0

在 LOL 客户端右下角显示 `✦` 设置面板，为选人网格、英雄卡片、备战席和队伍头像添加 OP.GG T 级角标，并提供壁纸和导航隐藏开关。插件只修改客户端 DOM / CSS，不执行选人、奖励或其他 LCU 写操作。

## 从助手安装

1. 在助手打开 **设置 → 客户端工具 → 资料与外观 → 客户端视觉插件（Pengu）**。
2. 点击“下载 Loader 并安装插件”。助手通过 GitHub 官方 Releases API 查询最新稳定版，下载便携 ZIP，解压并安装视觉插件，同时保存插件目录；支持进度显示和取消。
3. 点击“打开 Loader”，在 Loader 窗口内启用开关，然后完整重启 LOL 客户端。
4. 点击客户端右下角 `✦`。助手加载状态回传取决于 Loader 是否提供文件写入接口；未收到回传时，请在客户端确认显示，不能因此判定加载失败。

下载目录为 `%LOCALAPPDATA%/LOL-GameAssistant/PenguLoader/<版本>/`，不会放进 LOL / Riot Client 目录。重复下载同一版本会复用已有文件；下载失败或取消会清理临时文件。校验下载长度；官方接口提供 SHA-256 时同时校验摘要，未提供时只记录本地 SHA-256，不把本地摘要称为官方认证。助手不自动激活 Loader。

已有 Loader 时，可点击“选择目录”，选择其 `plugins` 文件夹或包含该文件夹的 Loader 根目录，再点击“安装 / 更新插件”。

安装包和更新包携带 `Tools/PenguPlugin/`。安装按钮将其中三个文件部署到 `plugins/lol-game-assistant-visuals/`，不会自动升级已部署的副本。升级助手后，需再次点击“安装 / 更新插件”并重启客户端；更新保留原有停用状态。

“停用”会将入口改名为 `index.js.disabled`，下次启动客户端生效。“卸载插件”只移除本插件已知文件，保留用户额外文件及客户端 localStorage 视觉偏好。已加载的客户端需要重启后才会移除面板。

也可手动把本目录的 `index.js`、`pengu.yml`、`README.md` 复制到上述插件目录。运行时不需要 Node.js、npm 或浏览器测试依赖。

## 客户端显示与数据更新

- 默认从本机 LCU 的 gameflow / lobby 读取队列，自动识别峡谷、大乱斗、斗魂竞技场、无限火力；不支持或尚未就绪的队列等待识别，不显示错误模式的数据。也可手动选择模式。
- 同一模式的数据缓存 30 分钟，每 30 秒检查模式与缓存；失败按 2、4、8 秒等间隔退避重试，最长 60 秒，可手动刷新。
- 切换模式清除旧角标，过期请求不会覆盖新数据；头像被复用或英雄 ID 改变后重新匹配。卸载会清理监听、计时器、请求、角标及插件修改的定位样式。
- 壁纸只接受 HTTP / HTTPS 图片地址，清空即恢复。客户端页面自身的不透明背景可能遮挡壁纸。
- 排位角标取该英雄公开统计中最好的分路 T 级，不代表玩家本人水平。

## 排查没有显示

助手中的“检查加载状态”会打开独立检查窗口，显示检查时间、插件安装与入口状态、Loader 版本、实际激活配置、LOL 客户端进程、Loader 模块所在目录及插件心跳，并给出下一步建议。检查在后台执行；读目录或进程失败时显示原因。窗口支持重新检查，重复点击入口会激活已有窗口。下载和安装插件不等于激活 Loader；Active Status 为 OFF 时必须在 Loader 中开启并接受客户端重启提示。

Loader 模块已加载只说明 Loader 进入了客户端，不代表视觉插件一定显示；未收到文件心跳时仍需在客户端确认 `✦`。如果检测到客户端使用了另一目录的 Loader，应将插件安装到那个 Loader 的 `plugins` 目录。

| 现象 | 检查方法 |
| --- | --- |
| 助手提示“已安装，尚未确认加载” | 文件存在不代表 Loader 已启用；检查 Loader 版本、插件目录，完整重启客户端 |
| 没有右下角 `✦` | 使用 Loader 的客户端开发者工具检查加载错误；确认入口文件为 UTF-8 无 BOM |
| 看得到 `✦`，点击无反应 | 更新插件后重新加载客户端；插件会检测客户端重建页面造成的节点替换，重新绑定交互 |
| 面板已显示但数据为 0，提示 HTTP / 超时 | 检查 OP.GG 网络访问；插件会重试，也可手动刷新 |
| 有数据但找到 0 个头像 | 先进入选人页；若仍为 0，当前客户端 DOM 可能变化，需要适配挂载选择器 |
| 有头像但没有角标 | 确认角标开关、模式及英雄统计是否存在；查看面板错误信息 |
| 客户端有面板，助手未确认加载 | `runtime.json` 需要 Loader 提供 `context.fs.write` 接口及插件目录写权限 |

Loader 提供文件写入接口时，插件每 10 秒向自身目录写入 `runtime.json`，只包含版本、时间、面板状态、模式、头像与角标数量和数据错误；不包含账号、令牌或聊天内容。助手只把 35 秒内的有效活动心跳视为“已加载”，不将其当作所有功能兼容的证明。

## 开发与验证

```powershell
# 无浏览器依赖的缓存竞态测试
node --test PenguPlugin/tests/tier-cache.test.cjs

# 安装开发依赖与测试浏览器（从仓库根目录运行）
npm ci --prefix PenguPlugin
node PenguPlugin/node_modules/playwright/cli.js install chromium
npm test --prefix PenguPlugin

# 或使用本机 Edge，省去 Chromium 下载
$env:PENGU_TEST_BROWSER_CHANNEL = 'msedge'
npm test --prefix PenguPlugin

dotnet test LOL-GameAssistant.UiTests/LOL-GameAssistant.UiTests.csproj --filter "FullyQualifiedName~PenguLoaderDownloadTests|FullyQualifiedName~PenguPluginTests|FullyQualifiedName~ClientToolsCategoriesFitAndRemovedToolsAreAbsent"
```

浏览器测试使用模拟客户端 HTML 和网络响应，验证真实 DOM 渲染、模式切换、动态头像、失败重试和卸载，并输出 `PenguPlugin/test-results/client-preview.png`。它不能替代当前 LOL 客户端、实际 OP.GG 网络以及 Loader 环境的实测。

接入依据：[Pengu 插件入口](https://pengu.lol/guide/javascript-plugin)、[LCU 请求](https://pengu.lol/guide/lcu-request)、[Socket 观察](https://pengu.lol/runtime-api/socket)、[PluginFS](https://pengu.lol/runtime-api/fs)。
