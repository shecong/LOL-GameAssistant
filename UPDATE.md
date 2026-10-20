# 自动更新实现与发布说明

完整操作步骤见[自动更新操作手册](自动更新操作手册.md)。当前服务器地址为 `http://159.75.148.241/`，目录采用“版本 → 架构”。

## 配置与地址

程序目录 `update-source.json`：

```json
{
  "serverBaseUrl": "http://159.75.148.241/",
  "allowHttp": true
}
```

IIS 根目录 `update.json` 是最新版本索引，包含 `schemaVersion: 1`、`version`，可选 `releaseNotes` 为更新弹窗中显示的纯文本说明。完整更新配置位于 `v版本号/win-x64|win-x86|win-arm64/update.json`，同目录提供对应 ZIP。根索引和完整配置不可互换。

更新说明填写在网站根目录的 `update.json`（即 `http://159.75.148.241/update.json`），不填在版本 / 架构目录的完整下载配置中。例如：

```json
{
  "schemaVersion": 1,
  "version": "1.0.3",
  "releaseNotes": "1. 优化聊天室进房间提示，不再写入聊天历史。\n2. 对局布局切换移至获取数据按钮旁边。"
}
```

以 UTF-8 保存，换行使用 JSON 的 `\n`，整个索引不能超过 64 KB。不填或留空时仍显示“此版本未提供更新说明”。客户端需先更新到包含 `releaseNotes` 读取逻辑的构建；旧客户端无法仅靠修改服务器 JSON 显示说明。重新启动客户端并检测到更高版本后，新打开的更新弹窗会读取说明，已打开的弹窗不会自动刷新。

打包时可准备 UTF-8 文本文件，并传入 `-ReleaseNotesFile`，脚本自动转换换行和引号：

```powershell
pwsh -File tools/publish-update-menu.ps1 -Architecture x64 -Mode Build `
  -ReleaseNotesFile 'build/release-notes.example.txt'
```

请先将示例内容改为实际版本说明，或传入自己的文本文件。底层 `tools/publish-update.ps1` 同样支持此参数。未传入时生成的根索引不包含说明。所有架构上传并验证后，最后上传生成的根索引 `update.json`。

```text
/update.json
/v6.1.1/win-x64/update.json
/v6.1.1/win-x64/LOL-GameAssistant-win-x64.zip
/v6.1.1/win-x86/update.json
/v6.1.1/win-x86/LOL-GameAssistant-win-x86.zip
/v6.1.1/win-arm64/update.json
/v6.1.1/win-arm64/LOL-GameAssistant-win-arm64.zip
```

`UpdatePlatform` 基于 `RuntimeInformation.ProcessArchitecture`，不是系统架构。配置、ZIP 名称、主程序 PE 架构均需匹配当前进程。不会通过自动更新切换架构。

GitHub 使用固定发布标签和独立附件：`update-win-x64.json`、`update-win-x86.json`、`update-win-arm64.json`，分别指向同标签对应 ZIP。x64 新配置附件 404 时回退旧 `update.json`；其他架构不做此回退。GitHub 版本检查仍保留 API 到网页重定向回退，不需要个人令牌。

## 发布命令与输出

BAT 入口将成品平铺到 E:/release-assets/版本号/：各架构 ZIP、update-win-*.json、根索引 update.json 和发布结果.txt。没有时间戳或重复架构目录；中间文件位于项目 .artifacts/updates/pack-work。IIS 上传时仍按 v版本号/架构/ 部署，完整配置在服务器改名为 update.json；GitHub 直接上传同层 ZIP 与 update-win-*.json。下面底层脚本示例默认保留嵌套布局；-FlatOutput 由 BAT 内部使用，根索引由 BAT 在全部任务成功后写入。

```powershell
pwsh -File native/Updater/build.ps1 -Runtime win-x64
pwsh -File tools/publish-update.ps1 -Runtime win-x64 `
  -PublishDirectory 'E:/releases/6.1.1/win-x64/publish' `
  -OutputDirectory 'E:/releases/6.1.1/win-x64/assets' `
  -ServerBaseUrl 'http://159.75.148.241/' -AllowHttpServer
```

`-Runtime` 支持三个 RID，默认 x64。版本统一读取 build/Version.props 的 SoftwareVersion，VS 自动生成程序集和文件版本。-Version 可省略，传入时必须与配置相同。默认复用 Tools/Updater/运行时/Updater.exe，也可用 -UpdaterPath 指定；仓库缺少时可读取已有发布目录中的更新器。仅 -RebuildUpdater 会重编更新器。已有发布目录时不重新发布主程序；不指定 `-PublishDirectory` 时执行自包含单文件发布。输出目录必须不存在。脚本检查 EXE 文件版本及主程序、更新器的 PE 架构，不能只改 JSON 冒充其他平台。

输出包括：IIS 根索引 `update.json`、GitHub 完整配置 `update-运行时.json`、IIS `v版本号/运行时/` 中的 ZIP 和完整配置、中间目录 `work/`。x64 额外输出 `github-legacy/update.json` 作为可选的旧客户端 GitHub 兼容附件，不能当作 IIS 根索引上传。

各架构独立打包，先合并上传所有准备发布的版本 / 架构目录，最后单独发布根索引。不要直接把整个输出目录覆盖到网站。GitHub 使用同版本同架构 ZIP，完整配置按架构独立命名。

VS 提供 `Windows-x64`、`Windows-x86`、`Windows-ARM64` 文件夹发布配置。原生更新器输出位于 `native/Updater/bin/运行时/`，副本位于 `LOL-GameAssistant/Tools/Updater/运行时/`，VS 按 RID 复制到程序根目录。

主程序仍使用 .NET 10。更新器为 C++20 / Win32，使用 /MT 静态链接，不依赖 .NET 或 VC++ Redistributable。日常发布复用现成更新器，不要求 MSVC；只有首次构建或源码变更后显式 -RebuildUpdater 才需要 MSVC、SDK 及对应架构编译器。更新模块支持多架构不代表所有业务组件已完成移植；SkinCore、GameOverlay 现有构建脚本仍面向 x64，其他架构需要独立验收 OCR 和原生依赖。本机已于 2026-10-18 安装 ARM64 编译工具，并成功生成原生 ARM64 更新器，实际运行仍需目标机器验收。

## 下载与可选安装

检测到新版后，主程序与关于页显示“有新版本更新”。服务器索引读取失败回退 GitHub；成功返回旧版本时不会再取 GitHub 的更高版本。弹窗可切换下载源，下载失败可切换后重试。索引不包含架构可用列表，缺少对应架构目录时下载会返回 404，发布者应同步准备已支持的架构。

下载、哈希校验和解压由主程序完成。验证固定版本的完整配置、大小、ZIP SHA256、逐文件清单、主程序文件版本和 PE 架构后准备任务，用户再点击“重启并安装”。下载期间不退出程序，选人 / 对局 / 重连阶段禁止安装。取消后重新打开窗口即可再试。

完成下载后选择“稍后再说”会保留安装包。重新打开同版本更新弹窗，或重启助手后再次检测更新，会校验并恢复本地已完成的下载，直接显示“重启并安装”，无需重新下载，也不要求原下载源在线。已准备任务保存在安装目录 `.update/<token>/`，新增 `manifest.json` 保存下载配置；恢复时重新核对 ZIP 哈希、解压文件清单、版本和架构，并刷新安装任务的当前进程信息。损坏、版本不符或未完成的下载不会被复用；已经进入安装流程的任务仍使用原生更新器恢复。此功能只适用于使用新版逻辑完成的下载，旧任务缺少缓存配置时仍需下载一次。

ZIP 根目录直接放主程序、Updater.exe、update-source.json、全部依赖和 `update-files.txt`，不能多套一层目录。清单为 UTF-8，每行 SHA256、TAB、相对路径，不包含自身。下载器拒绝路径穿越、符号链接、重复路径、清单外文件、错误哈希及版本平台不符。

公网 HTTP 需显式 allowHttp；SHA256 提供完整性检查，不能防止 HTTP 配置和 ZIP 同时被网络篡改。HTTPS 也不等于发布签名，本版没有签名清单。

## 事务与恢复

任务位于 `安装目录/.update/<token>/`，包含 package.zip、stage、backup、runner、job.ini、rollback.tsv、state.txt、updater.log。主程序只运行本机自带的原生更新器副本。

`job.ini` 使用 UTF-16，记录 token、主程序版本、父进程 PID 与创建时间、文件清单哈希，安装路径由任务目录推导。更新器验证任务后发送 ready，主程序才退出；等待旧进程最多 60 秒。

```text
ready → backing_up → applying → starting → completed
                         └─失败→ rolledback
                               └─恢复失败→ recovery_failed
```

先完整备份并持久化恢复清单，再逐文件替换。整个包不是一次原子操作，断电后按日志恢复。只删除上版清单中存在、新版移除的托管文件；不覆盖用户设置、日志、缓存和未托管文件。

新版主窗口初始化并经历一次消息循环后确认启动。更新器等待最多 60 秒，并继续观察约 1.5 秒；启动未确认或提前退出则尝试恢复旧文件并重启旧版。确认不代表所有业务功能通过。

中断恢复：关闭助手，双击根目录 Updater.exe；损坏时使用对应 `.update/<token>/runner/Updater.exe --recover job.ini`。备份损坏时停止并保留现场。正常主程序发现 active-job 标记会要求恢复。

成功和失败任务保留日志与备份，不自动清理。任务为 completed / rolledback 且 active-job.txt 已消失后才可清理对应历史目录。用户设置通常位于 `%LocalAppData%/LOL-GameAssistant/settings.json`，文件回滚不回滚用户数据。

## 验证

```powershell
pwsh -File native/Updater/build.ps1 -Runtime win-x64 -Test
pwsh -File native/Updater/build.ps1 -Runtime win-x86 -Test
dotnet test LOL-GameAssistant.UiTests/LOL-GameAssistant.UiTests.csproj `
  --filter 'FullyQualifiedName~Update|FullyQualifiedName~AboutPageTests'
```

运行原生测试需要主机能执行目标架构程序。ARM64 应在对应环境运行，不能把交叉编译等同于运行验收。正式发布前按操作手册完成服务器下载及低版本测试副本的实际更新验证。
