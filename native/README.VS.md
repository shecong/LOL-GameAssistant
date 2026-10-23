# 在 Visual Studio 中构建换肤核心

打开仓库根目录 `LOL-GameAssistant.slnx`，展开“换肤核心”分组：

主程序使用 .NET 10，整个解决方案在 VS 内构建需要 VS 2026 / MSBuild 18 及 .NET 10 SDK。仅构建三个原生 Makefile 工程也可使用安装了 C++ 工具的 VS 2022；它不能代替主程序的 .NET 10 工具链。本次在本机 VS 2022 Build Tools 下验证了三个原生工程的独立生成，以及 `dotnet build LOL-GameAssistant.slnx --no-restore` 的默认 Any CPU 构建。

- `LeagueSkinCore.Classic`：经典模式的主机与 DLL。
- `LeagueSkinCore.Hybrid`：混合模式的主机、DLL 与测试客户端。
- `SkinCoreRelay.Driver`：混合模式的 KMDF 消息中继驱动。

三个工程是 C++ Makefile 工程，源码可直接在 VS 中编辑；右键“生成”“重新生成”“清理”会调用仓库的 PowerShell 构建脚本。需要安装 VS 的 C++ 桌面开发工具。原有构建脚本使用优化参数，Debug/Release 方案配置目前都沿用这些参数，不会自动切成原生调试编译。

选择 **x64** 后生成解决方案，会先生成经典和混合核心，再生成助手。生成的运行文件自动复制到主程序 `Tools/SkinCore` 和 `Tools/SkinCoreHybrid`，随后由主程序复制到输出目录。**Any CPU** 继续沿用已准备的原生运行文件，不默认构建 C++ 工程。

驱动默认不参与解决方案生成，需要时右键 `SkinCoreRelay.Driver` 单独生成。它要求 SDK/WDK 和 KMDF 1.31；自动查找本机 WDK 或本仓库已经准备的 `.artifacts/wdk-nuget/wdk/c`。可通过环境变量或 MSBuild 属性 `SkinWdkRoot` 与 `SkinSdkVersion` 指定其他路径/版本。

“清理”只删除各工程 `bin` 下已知的编译输出，不删除部署到 `Tools` 的运行文件或原版备份。“重新生成”重新运行完整编译脚本。驱动生成只产生未签名 SYS，不安装、加载或修改系统设置；签名及动态验收要求见 `SkinCoreRelay/README.md`。

命令行构建 C++ 工程需使用 Visual Studio 的 `MSBuild.exe`（不是 `dotnet build` 的 C++ 构建入口）：

```powershell
MSBuild.exe native/LeagueSkinCore/LeagueSkinCore.vcxproj /p:Configuration=Release /p:Platform=x64
MSBuild.exe native/LeagueSkinHybrid/LeagueSkinHybrid.vcxproj /p:Configuration=Release /p:Platform=x64
MSBuild.exe native/SkinCoreRelay/SkinCoreRelay.vcxproj /p:Configuration=Release /p:Platform=x64
```
