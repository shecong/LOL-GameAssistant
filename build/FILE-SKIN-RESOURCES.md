# 游戏外换肤资源独立更新

程序打开“游戏外换肤”页面时检查在线版本，也可点击“检查更新”。检查失败会明确显示失败并使用上次有效清单或内置资源，不会把检查失败显示成“已是最新”。使用现有皮肤资源不依赖检查成功。

发现更新后点击“下载资源”安装，不会自动在对局中替换资源。相同 ZIP 的 SHA256 已安装时，不重复下载；仅打开页面不会下载大资源包。

## 更新清单

客户端的 `update-source.json` 中，`fileSkinResources.manifestUrls` 指向固定清单地址。当前使用仓库 `master` 分支的 `build/file-skin-resources.json`，默认包下载源是可访问的 GitHub Release，已移除返回 404 的服务器 ZIP 地址。

清单字段：

- `version`：独立资源版本，数字点分格式，例如 `26.1115.1`，每次资源变化递增。
- `gamePatch`：资源目录标注的游戏版本；未知可为空，不能把主程序版本当作游戏版本。
- `urls`：同一个不可变 ZIP 的下载源，按顺序失败切换；所有源必须与同一校验值一致。
- `sha256`：整个 ZIP 的 SHA256。
- `size`：整个 ZIP 字节数。
- `notes`：此次资源更新说明。

主程序中的 `fileSkinResources` 同时保留一个已知可用包作为首次断网时的回退。在线检查缓存位于 `%LOCALAPPDATA%/LOL-GameAssistant/FileSkinResources/latest-resource.json`。资源版本低于已知版本的镜像会被忽略。

## 发布顺序

1. 准备新的皮肤库、索引与配套工具，确认资源的游戏版本。
2. 运行下面的命令；每次使用新资源版本、标签和输出目录，避免覆盖已发布附件。

```powershell
./tools/package-file-skin-resources.ps1 `
  -ResourceVersion '26.1115.1' `
  -OutputDirectory '.artifacts/file-skin-resources-26.1115.1' `
  -ReleaseNotes '本次资源变更说明'
```

`gamePatch` 默认从皮肤目录 `manifest.json` 的 `patch` 读取，也可以通过 `-GamePatch` 指定。脚本生成 ZIP、包含逐文件哈希的 `resource-files.json`、包内版本信息和外部更新清单。

3. 先将 ZIP 发布为 `file-skin-resources-<资源版本>` 标签下的 GitHub Release 附件。资源 Release 不要设为主程序的最新发行版。
4. 确认附件可下载后，把生成的 `file-skin-resources.json` 替换到仓库 `build/file-skin-resources.json` 并推送。已支持本机制的主程序无需重新发布。
5. 在助手中检查更新并验证下载安装。需要镜像时，先部署并验证服务器资源地址，再使用脚本的 `-IncludeServerMirror`，并将新地址放入清单。

不要在同一个版本 URL 下替换 ZIP 后继续沿用旧哈希。尚未升级到本更新机制的旧主程序仍需更新一次主程序，之后才能读取独立清单。

## 下载与安装

- 文件使用 ZIP 哈希命名，失败或主动取消保留断点，重试和换源均发送 Range 请求。服务器忽略 Range 时从头写入，避免错误拼接；所有完成文件仍校验完整 SHA256。
- 每个请求和正文读取最长允许 40 秒无响应；每次传输尝试总上限 30 分钟。每个源最多尝试两次，404/403 直接换源。
- 清单最大 64 KB，包与解压总量最大 2 GB；解压使用路径白名单、声明长度和逐文件哈希检查。
- 安装前后检查对局与覆盖层状态，验证目录后原子切换 `current.txt`，失败保留旧资源。下载与安装均有进程间文件锁，当前实例安装与应用互斥。
- 保留当前版和前一版，只清理此安装器管理的旧 GUID 目录，不跟随目录链接；过期七天的下载断点在安装成功后清理。
- 版本不明的旧缓存可继续使用；新版安装后保存资源版本和 ZIP 哈希。手动导入支持旧 ZIP，也支持新版包内版本信息。

以上检查不代表资源适配了任意游戏版本；皮肤内容是否正确仍需按资源标注的游戏版本实际验收。
