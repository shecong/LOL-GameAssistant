# SkinCoreRelay 驱动与双模式使用

这是一个 Non-PnP KMDF **消息中继驱动**。混合模式的请求与结果经过驱动，游戏函数仍由进程内 DLL 执行。驱动不提供任意进程内存读写、内核执行或模块加载接口。

## 构建

在仓库根目录运行：

```powershell
powershell -NoProfile -File native/LeagueSkinHybrid/build.ps1 -Test
powershell -NoProfile -File native/SkinCoreRelay/build.ps1 -WdkRoot '你的WDK根目录'
```

WDK 根目录需同时具有 `Include/<SDK版本>/km`、`Lib/<SDK版本>/km/x64` 及 KMDF 1.31 的头文件和库；也可使用微软 `Microsoft.Windows.WDK.x64` NuGet 包中的 `c` 目录。配套用户态 SDK 默认使用本机 10.0.26100.0。编译脚本不安装工具链、不修改启动设置、不签名和加载驱动。

输出：

- `native/LeagueSkinHybrid/bin/league-skin-hybrid-host.exe` 和 `league-skin-core.dll`，自动复制到 `LOL-GameAssistant/Tools/SkinCoreHybrid/`。
- `native/SkinCoreRelay/bin/SkinCoreRelay.sys`，编译后复制到上述混合运行目录的 `Driver/`，**这是未签名开发输出，不能作为已可用驱动分发**。
- `relay-test-client.exe --protocol-only` 只验证公共协议布局；不连接驱动。
- 混合构建的 `-Test` 还会在用户态模拟环境运行真实 `RelayIoctl` 分发代码，覆盖身份约束、重放、取消、超时、配额及引用释放；它不模拟 Windows ACL 和内核调度。

## 两种使用方式

游戏换肤页面增加“换肤方式”：

1. **经典模式（原有核心）**：继续使用 `Tools/SkinCore` 原文件及共享内存通信。
2. **混合模式（内核通信 + 进程内执行器）**：使用独立 `Tools/SkinCoreHybrid` 文件及 `SkinCoreRelay` 驱动传递请求和结果。

默认经典模式；旧设置缺失/无效时也回到经典模式。选择会保存到设置。只能在换肤关闭且未进入对局时切换。新模式失败不会自动切换后端或重放写请求。两种模式使用相同游戏构建配置和换肤业务源码；新增驱动不会自动支持新游戏版本，也不能替代被目标环境拒绝的进程内加载通道。

首次开发验收应在配置了驱动测试环境的虚拟机中完成。正式使用必须具备满足目标 Windows 策略的有效签名和兼容验证。安装脚本不会开启测试签名、关闭内存完整性或规避加载拒绝。

管理员 PowerShell，在签名和测试已经完成后：

```powershell
# DriverPath 必须是已正确签名、固定存放且普通用户不可替换的文件。
.\manage.ps1 -Action Install -DriverPath 'C:\Program Files\SkinCoreHybrid\Driver\SkinCoreRelay.sys'
.\manage.ps1 -Action Status
# 关闭所有混合通道请求后卸载。
.\manage.ps1 -Action Uninstall
```

当前版本没有安装常驻特权代理服务，混合主机打开驱动写句柄需要管理员权限，因此混合模式需要以管理员身份运行助手。目标执行器只需读权限。经典模式没有因此增加提权要求。后续可用受限服务隔离特权；不能将本版本描述为已实现该服务。

设备读权限只开放给交互用户，提交/查询/取消 IOCTL 要求写权限，仅管理员及 SYSTEM 可用。驱动为目标进程保留引用，领取/完成请求核对真实请求进程对象，避免只按 PID 判断。查询/取消同时核对提交进程和所属文件对象；每个提交句柄最多一个请求，全局最多四个，每条结果小于 1MB。关闭提交句柄即撤销记录并释放引用。

## 验证边界

在驱动已正确签名并加载的测试 VM 内，以管理员身份运行 `relay-test-client.exe`，它使用自己的进程完成提交、领取、结果返回和句柄撤销，不连接游戏。然后进行 Driver Verifier、HVCI、并发关闭/目标退出、异常长度与越权测试，最后进行授权环境下的游戏视觉回归。

驱动本身的静态编译和协议测试不能替代上述验收。本次实现没有在当前电脑安装、加载或动态验证驱动，也没有进行真实游戏换肤。

`league-skin-hybrid-host.exe driver-status` 可独立检查设备权限及协议，不连接游戏、不安装驱动。

## 原版备份

修改前完整备份位于仓库 `.artifacts/skin-core-backup-2026-10-23/`，含原生源码、原运行文件、C# 换肤服务和主界面源文件，附 `SHA256SUMS.json`。该目录作为本地恢复材料被 Git 忽略。现有 `native/LeagueSkinCore` 和 `Tools/SkinCore` 未被新模式构建覆盖。

混合执行器通过源码包含复用当前核心，后续核心修复后需要重新运行混合构建脚本，避免两个运行目录版本不一致。
