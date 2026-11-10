# sc助手 Windows 安装版

双击安装 EXE，在向导开始时选择简体中文或 English。版权页随安装语言切换。默认目录为 `C:\Program Files\sc助手`，可在目录页修改。首次安装时，创建桌面快捷方式和安装完成后启动默认勾选，可手动取消。开始菜单和 Windows 卸载入口由安装程序创建。

安装包支持 Windows x64、x86 和 ARM64。它会检测对应架构的 .NET 10 Desktop Runtime。缺少时，从微软下载并校验运行时安装器。游戏外换肤的大型资源在程序内下载或导入。

重新运行安装包可覆盖安装到原目录。应用内更新沿用便携版的服务器和 GitHub 下载源，按程序架构下载 ZIP 并更新当前安装目录。设置和已下载资源由程序保存在原有位置。卸载会移除安装程序记录的文件和快捷方式，保留用户数据及系统运行时。

## 重新构建

在仓库根目录运行以下命令。

```powershell
pwsh -File tools/build-installer.ps1
pwsh -File tools/build-installer.ps1 -Runtime win-arm64
.\publish-update.bat all -Mode Build
```

构建脚本读取 `build/Version.props`，发布所选架构的依赖框架版并调用 Inno Setup 编译。需要 .NET 10 SDK 和 Inno Setup 6.7 或兼容的编译器。单独构建安装包时可用 `-CompilerPath` 指定 `ISCC.exe`，用 `-OutputDirectory` 指定输出目录。通过发布 BAT 构建时，安装包和 ZIP 会输出到同一个版本目录。

单独构建的默认产物位于 `.artifacts/installers/<版本>/<架构>/`，包含安装 EXE 和 SHA-256 文件。运行时下载信息在 `installer/runtime-*.json`，更新时应同步替换版本、微软下载地址及校验值。
