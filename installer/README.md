# sc助手 Windows 安装版

双击安装 EXE，在向导开始时选择简体中文或 English。版权页随安装语言切换。默认目录为 `C:\Program Files\sc助手`，可在目录页修改。首次安装时，创建桌面快捷方式和安装完成后启动默认勾选，可手动取消。开始菜单和 Windows 卸载入口由安装程序创建。

安装包支持 Windows x64、x86 和 ARM64。它会检测对应架构的 .NET 11 Desktop Runtime。缺少时，从微软下载并校验运行时安装器。游戏外换肤的大型资源在程序内下载或导入。

重新运行安装包可覆盖安装到原目录。应用内更新沿用便携版的服务器和 GitHub 下载源，按程序架构下载 ZIP 并更新当前安装目录。设置和已下载资源由程序保存在原有位置。卸载会移除安装程序记录的文件和快捷方式，保留用户数据及系统运行时。

## 重新构建

在仓库根目录运行以下命令。

```powershell
pwsh -File tools/build-installer.ps1
pwsh -File tools/build-installer.ps1 -Runtime win-arm64
.\publish-update.bat all -Mode Build
```

构建脚本读取 `build/Version.props`，发布所选架构的依赖框架版并调用 Inno Setup 编译。需要 .NET 11 SDK 和 Inno Setup 6.7 或兼容的编译器。单独构建安装包时可用 `-CompilerPath` 指定 `ISCC.exe`，用 `-OutputDirectory` 指定输出目录。通过发布 BAT 构建时，安装包和 ZIP 会输出到同一个版本目录。

单独构建的默认产物位于 `.artifacts/installers/<版本>/<架构>/`，包含安装 EXE 和 SHA-256 文件。运行时下载信息在 `installer/runtime-*.json`，更新时应同步替换版本、微软下载地址及校验值。

## GitHub 与服务器目录

`publish-update.bat all -Mode Build` 会生成三种架构的更新 ZIP、安装 EXE 和校验文件。版本目录根部保留现有 GitHub 附件格式，同时自动增加 `server` 文件夹。它复用同一份成品，不会再次压缩 ZIP 或编译安装 EXE。

已有成品也可以单独整理，无需重新发布：

```powershell
pwsh -File tools/prepare-server-release.ps1 -AssetDirectory E:\release-assets\26.1109.2
```

导出前会验证更新清单、ZIP 大小与 SHA-256、安装包 SHA-256。目录如下，每个架构下均包含对应 ZIP、安装 EXE、安装包 SHA-256 和完整更新清单：

```text
26.1109.2/                         # GitHub 附件仍在版本目录根部
  LOL-GameAssistant-win-*.zip
  update-win-*.json
  sc-assistant-setup-26.1109.2-win-*.exe
  sc-assistant-setup-26.1109.2-win-*.exe.sha256
  server/                         # 此目录的内容对应服务器网站根目录
    update.json                   # 根更新索引
    v26.1109.2/
      win-x64/update.json
      win-x86/update.json
      win-arm64/update.json
      ...                         # 各架构的 ZIP、EXE、SHA-256
```

将 `server` 内的版本文件夹直接复制到服务器网站根目录，最后覆盖根目录的 `update.json`。现有其他版本和独立换肤资源目录无需移动或删除。只需整理更新 ZIP 时可加 `-SkipInstaller`；默认会要求三种架构的安装包齐全。
