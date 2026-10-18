# 自动更新实现与发布操作

面向实际发布的逐步教程见[自动更新操作手册](自动更新操作手册.md)，包含当前 IP / IIS 配置、VS 发布、上传、验证、首次分发和故障恢复。本文保留实现细节与开发说明。

## 组成

主程序仍使用 C# / .NET 10。下载、SHA256 校验、解压和下载进度由主程序完成。更新器使用 C++20 / Win32，静态链接 MSVC 运行库，无 .NET 运行时依赖。`Updater.exe` 随完整包放在主程序旁，安装时复制到独立事务目录运行，所以可以更新安装目录中的更新器自身。

代码入口：

- `LOL-GameAssistant/Infrastructure/ApplicationInfo/UpdatePackageService.cs`：读取配置、流式下载、解压及逐文件验证。
- `LOL-GameAssistant/Helper/UpdateInstaller.cs`：启动握手、安装中断保护和新版启动确认。
- `LOL-GameAssistant/BaseViewForm/UpdateAvailableForm.cs`：下载、取消、重启安装及原有下载/QQ群入口。
- `native/Updater/updater.cpp`：等待旧进程退出、备份、文件替换、旧托管文件删除、启动确认与恢复。
- `tools/publish-update.ps1`：统一构建和生成发布附件。

## 用户流程

检测到新版本后，主窗口和关于页继续显示“有新版本更新”。用户可选择“下载更新”、GitHub 下载、QQ群获取或关闭提示。下载阶段不退出主程序，也不修改已安装文件。下载校验完成后，按钮变为“重启并安装”，由用户再次点击决定安装时间。

选人、对局进行中和重连阶段禁止安装，但可以下载。关闭窗口或点击取消可以中止下载；取消后重新打开更新窗口即可重试。网络、配置或校验失败时继续使用旧版。

主程序与更新器通过带随机 token 的 Win32 命名事件握手。更新器先验证任务和文件，主程序收到 ready 后才退出。更新器最多等待旧进程退出 60 秒，不会为了更新强制结束游戏或其他现有进程。文件占用最多重试约 10 秒，失败则恢复；请关闭仍占用运行包的配套工具后再试。

## 构建环境

开发机安装：

1. Windows x64、PowerShell 7。
2. .NET 10 SDK，用于构建主程序。
3. Visual Studio 2022 或更新版本 / Build Tools，勾选“使用 C++ 的桌面开发”，包括 MSVC x64 编译器和 Windows 10/11 SDK。

用户电脑运行原生更新器不需要安装 SDK、.NET 或 VC++ Redistributable。主程序发布为自包含完整包，包含自己的 .NET 运行时。

单独编译和验证更新器：

```powershell
pwsh -File native/Updater/build.ps1
pwsh -File native/Updater/build.ps1 -Test
```

生成位置：`native/Updater/bin/Updater.exe`，脚本同时复制到 `LOL-GameAssistant/Tools/Updater/Updater.exe`。主程序构建/发布会将其复制到输出根目录。首次启用自动安装之前，必须分发一次包含此更新器的新完整包；旧运行包缺少更新器时会提示手动获取，不执行下载的未知更新器。

## 一次完整发布

版本必须比当前已发布版本大。以下以 `6.1.0` 为例，不代表已经发布这个版本。

```powershell
# 从仓库根目录运行；会编译更新器、发布主程序并生成附件。
pwsh -File tools/publish-update.ps1 -Version 6.1.0
```

默认输出：

```text
.artifacts/updates/6.1.0/
  LOL-GameAssistant-win-x64.zip  # 上传
  update.json                   # 上传
  package/                      # 打包中间目录，不上传
  publish/                      # dotnet publish 输出，不上传
```

输出目录已存在时脚本停止，避免意外覆盖包；重建时使用 `-OutputDirectory` 指定新的目录。也可对已有的干净发布目录打包：

```powershell
pwsh -File tools/publish-update.ps1 -Version 6.1.0 `
  -PublishDirectory E:/releases/6.1.0/publish `
  -OutputDirectory E:/releases/6.1.0/assets
```

已有发布目录的 EXE 文件版本必须匹配 `6.1.0.0`。不要拿开发运行目录打包，里面可能包含个人配置；脚本拒绝已知用户数据目录和 `settings.json`。`PublishDirectory` 中的所有其他非 PDB 文件都视为受管理程序文件。

接着在 GitHub 仓库 `shecong/LOL-GameAssistant` 创建一个草稿 Release：

1. 标签填 `v6.1.0`（或用脚本 `-Tag 6.1.0` 保持标签一致）。
2. 填写更新说明。
3. 上传上述 ZIP 和 `update.json`，两个附件名必须保持不变。
4. 确认两个附件都上传完成，再发布正式 Release 并设为 Latest。

仅生成 Git tag 或上传 GitHub 自动生成的源码 ZIP 不支持自动安装。已发布的同版本包应保持不变；修复包发布新版本。脚本通过构建参数注入程序集版本，关于页和版本比较使用发布的真实版本；建议同步项目 `<Version>` 方便开发时显示。

## 服务器下载（保留 GitHub）

支持 Nginx、IIS、对象存储或 CDN 托管静态文件，无需编写服务端接口。配置服务器后优先读取服务器版本，连接失败、配置无效或超时时回退 GitHub。更新弹窗增加“下载源”，服务器检查成功时默认选服务器，检查失败回退后默认选 GitHub，也可手动切换。下载失败会保留错误信息，用户可切换源重试；安装、SHA256 校验和回滚逻辑共用。

程序目录中的 `update-source.json` 配置如下（空字符串表示仅使用 GitHub）：

```json
{
  "serverBaseUrl": "https://download.example.com/lol-assistant/"
}
```

此文件随 VS 文件夹发布复制到输出目录，可在发布前修改项目中的同名文件；它属于随软件发布的部署配置，后续版本应保持相同地址。默认要求 HTTPS；公网 HTTP 需要显式设置 `allowHttp: true`。地址不能含查询参数、账号密码或片段。HTTP 下 SHA256 只能检查文件完整性，无法防止配置和 ZIP 同时遭到网络篡改；后续可迁移 HTTPS。

也可通过打包参数写入更新包中的配置，已有的 VS 发布目录不会被修改：

```powershell
pwsh -File tools/publish-update.ps1 -Version 6.1.0 `
  -PublishDirectory E:/releases/6.1.0/publish `
  -OutputDirectory E:/releases/6.1.0/assets `
  -ServerBaseUrl "https://download.example.com/lol-assistant/"
```

将生成的同一组 ZIP 和 `update.json` 上传 GitHub，也部署到服务器：

```text
https://download.example.com/lol-assistant/
  update.json                     # 最新版本指针：与最新版本目录中的配置内容相同
  v6.1.0/
    update.json
    LOL-GameAssistant-win-x64.zip
  v6.2.0/
    update.json
    LOL-GameAssistant-win-x64.zip
```

每次先上传完整的 `v版本号/` 目录并确认可下载，最后用同一份 `update.json` 覆盖根目录文件。根目录配置建议设置 `Cache-Control: no-cache`；版本目录保持不变，可长期缓存。不能只替换 ZIP 而保留旧哈希；修复包请增加版本号。Web 服务器应允许匿名 GET，并正确返回 ZIP 内容，不能返回登录页面或下载中转 HTML。

检查请求根目录 `update.json`，实际下载固定请求 `v版本号/update.json` 和同目录 ZIP，避免用户下载期间发布新版本导致配置与 ZIP 错配。没有服务器配置时，现有 GitHub 行为保持不变。服务器尚未配置真实域名和上传凭据，本仓库不会自动部署文件。

### 当前 IP / IIS 根目录部署

当前项目的 `update-source.json` 已设置为：

```json
{
  "serverBaseUrl": "http://159.75.148.241/",
  "allowHttp": true
}
```

将文件放到 IIS 对应网站的物理根目录（以默认 `C:\inetpub\wwwroot` 为例；实际以网站“基本设置”中的物理路径为准）：

```text
C:\inetpub\wwwroot\
  update.json
  v6.1.0\
    update.json
    LOL-GameAssistant-win-x64.zip
```

根目录的 `update.json` 与 `v6.1.0` 内的内容相同。发布新版本时保留旧版本目录，先上传新的版本目录，最后更新根目录配置。

VS 发布后可以运行：

```powershell
pwsh -File tools/publish-update.ps1 -Version 6.1.0 `
  -PublishDirectory E:/releases/6.1.0/publish `
  -OutputDirectory E:/releases/6.1.0/assets `
  -ServerBaseUrl "http://159.75.148.241/" -AllowHttpServer
```

IIS 网站需要绑定 HTTP 80 端口且主机名留空（或另建匹配 IP 的绑定），允许匿名读取静态文件，云服务器安全组和 Windows 防火墙放行该端口。无需开启目录浏览。确认 `.json` 的 MIME 类型是 `application/json`、`.zip` 是 `application/zip`；已有映射无需重复添加。浏览器应能直接读取 `http://159.75.148.241/update.json`，并下载 `http://159.75.148.241/v6.1.0/LOL-GameAssistant-win-x64.zip`。

## 更新配置

`build/update.example.json` 是说明用样例；实际上传的配置由发布脚本计算，不要手填示例中的体积或全零哈希。

```json
{
  "schemaVersion": 1,
  "version": "6.1.0",
  "runtime": "win-x64",
  "package": "LOL-GameAssistant-win-x64.zip",
  "size": 12345678,
  "sha256": "64位十六进制SHA256，脚本自动生成",
  "entryExecutable": "LOL-GameGUI.exe"
}
```

下载地址使用检测出的固定标签，而不是下载过程中可能变化的 Latest：

```text
https://github.com/shecong/LOL-GameAssistant/releases/download/v6.1.0/update.json
https://github.com/shecong/LOL-GameAssistant/releases/download/v6.1.0/LOL-GameAssistant-win-x64.zip
```

版本检测仍保留现有 API + 网页重定向回退。附件下载直接使用 GitHub Release 的网页下载地址，无需个人访问令牌，也不依赖 API 配额。SHA256 用于检查文件完整性；信任来源是 HTTPS 下的该 GitHub 仓库，不能替代发布签名。若需要更强的发布身份验证，后续可加入签名清单。

ZIP 根目录直接放程序，不再套一层目录：

```text
LOL-GameGUI.exe
Updater.exe
Tools/SkinCore/...
Resources/...
update-files.txt
```

`update-files.txt` 由脚本生成，每行 `SHA256<TAB>相对路径`，UTF-8 无 BOM。下载器拒绝重复路径、目录穿越、符号链接、清单外文件、错误哈希和主程序版本不符；更新器替换前再次验证。清单不包含自身，通过本地任务中的清单 SHA256 校验。

## 事务与恢复

每次任务位于 `安装目录/.update/<随机token>/`，临时包、stage、backup、runner、`job.ini`、`rollback.tsv`、`state.txt`、`updater.log` 都在这里。主程序运行目录需要可写；主程序本来就以管理员身份运行，原生更新器继承权限，手动恢复时请求 UAC。

本地任务使用 UTF-16 INI（不是远端的 update.json），以便原生程序无需携带 JSON 依赖。路径由任务所在目录推导，程序不能传任意安装目的地。`ParentPid` 和 `ParentStart` 一起验证旧进程身份。

状态流转：

```text
ready → backing_up → applying → starting → completed
                         └─失败→ rolledback
                               └─恢复失败→ recovery_failed
```

先备份所有将被修改或删除的托管文件并写入持久化恢复清单，再逐个使用临时文件 + `MoveFileExW` 替换。整包替换不是一次原子操作，断电或强制结束后需要按记录恢复。仅删除上个 `update-files.txt` 中存在而新版本移除的文件；未被清单管理的用户文件不删除。

新版在主窗口初始化并经过一次消息循环后发出启动确认。更新器等待最多 60 秒，并继续观察约 1.5 秒；未确认或提前退出则结束本次刚启动的新版、恢复旧文件并重新启动旧版。该确认只覆盖主窗口启动，不代表所有联网功能正常。

配置通常位于 `%LocalAppData%/LOL-GameAssistant/settings.json`，更新不触碰它。日志、缓存和根目录旧 `settings.json` 也不覆盖。程序文件可以恢复，但跨版本的配置格式迁移仍应保持向后兼容；本版不回滚用户数据。

### 安装失败或断电后

1. 关闭助手。
2. 双击安装目录中的 `Updater.exe`，自动读取 `.update/active-job.txt` 并恢复。
3. 如果安装目录的更新器损坏，运行事务中保留的副本：

```powershell
& 'E:/LOL-GameAssistant/.update/<token>/runner/Updater.exe' `
  --recover 'E:/LOL-GameAssistant/.update/<token>/job.ini'
```

必须使用该任务的真实路径。恢复先验证全部备份，备份缺失或损坏时停止并保留现场，不删除剩余备份。正常主程序检测到未完成任务时会提示恢复，避免在部分替换状态下继续运行。

成功与失败任务都保留日志和备份便于排查，本版不自动删除事务目录。确认 `state.txt` 是 `completed` 或 `rolledback`、且 `active-job.txt` 已消失后，可手动删除对应的历史 token 目录释放空间。不要在安装/恢复过程中清理 `.update`。

## 验证命令

```powershell
pwsh -File native/Updater/build.ps1 -Test
dotnet test LOL-GameAssistant.UiTests/LOL-GameAssistant.UiTests.csproj --filter 'FullyQualifiedName~Update'
```

建议发布前在单独的测试目录里安装旧版并更新一次，检查取消下载、错误 SHA256、文件占用、新版启动失败和恢复操作。不要直接在源码目录中执行替换测试。
