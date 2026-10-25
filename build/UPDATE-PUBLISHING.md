# 自动更新发布

入口仍为仓库根目录 `publish-update.bat`，需 PowerShell 7。版本号统一读取 `build/Version.props`。

```powershell
# 检查构建环境和现成更新器，不生成发布包
.\publish-update.bat x64 -Mode Build -CheckOnly
# 自动构建主程序并生成更新包（默认 E:\release-assets\版本号）
.\publish-update.bat x64 -Mode Build
# 打包 VS 已发布到干净目录的主程序
.\publish-update.bat x64 -PublishDirectory 'E:\releases\版本号\win-x64\publish'
# 更新器源码或程序入口名变更后，重新构建对应架构的更新器
.\publish-update.bat x86 -Mode Build -RebuildUpdater
```

`all` 会检查全部架构，任一架构未就绪会在生成更新包前停止。
ARM64 重建需安装 MSVC ARM64 编译工具和对应 Windows SDK 库；不能复用 x64/x86 的更新器。构建脚本会遍历已安装的 VS/Build Tools 及 MSVC 版本，选择具备目标架构的工具链，不再只检查最新 IDE。
双击 BAT 使用默认输出目录时，若同版本目录已经存在，会自动生成带时间和随机后缀的新目录，保留旧产物。命令行显式指定输出根目录时仍禁止覆盖。
`-RebuildUpdater -CheckOnly` 只检查编译环境；实际兼容性检查在构建后打包时执行。

## 程序改名的影响

- 当前主程序是 `sc助手.exe`，更新配置的 `entryExecutable`、客户端校验和原生更新器必须一致。仅手动重命名旧 EXE 不足以完成迁移。
- 打包脚本读取项目的 `AssemblyName` 并核对当前协议入口，还会检查所复用更新器中是否包含当前入口名称，拦截仍只接受 `LOL-GameGUI.exe` 的旧更新器。
- 2026-10-25 检查发现本机 x86、ARM64 现成更新器仍使用旧名，两者现已重新构建。ARM64 编译器位于 VS 2022 Build Tools，旧构建脚本只检查最新 VS 2026 而误报缺失；工具链查找已修正。
- 本机 `E:\release-assets\26.1025.1` 的 x86、ARM64 ZIP 也包含旧更新器，不能继续作为改名后的完整发布包使用。重建更新器后需要重新打包，使用新的输出目录；脚本不会覆盖已有附件。
- 改名前、只接受 `LOL-GameGUI.exe` 的客户端无法直接安装新入口包，需要下载一次完整包并运行 `sc助手.exe`。新包附带的新更新器不能修复旧客户端首次迁移，因为安装任务使用的是本机旧更新器的副本。
- ZIP 附件保持 `LOL-GameAssistant-win-x64.zip` / `win-x86.zip` / `win-arm64.zip`。这是客户端使用的协议名称，不应随展示名称改动。

## 上传顺序

IIS：将 ZIP 和对应的 `update-win-*.json` 上传到 `v版本号/win-架构/`，在服务器把完整配置命名为 `update.json`。所有架构验证后，最后上传输出根目录的 `update.json` 版本索引。

GitHub：ZIP 和 `update-win-*.json` 作为对应版本 Release 附件上传。旧 x64 清单文件名兼容可另外复制完整的 `update-win-x64.json` 为 `update.json`；这仅兼容清单名称，不能解决 EXE 改名迁移。不要上传根目录的版本索引来代替完整清单。

脚本回归检查：`pwsh -NoProfile -File tools/tests/UpdatePublishing.Tests.ps1`。
