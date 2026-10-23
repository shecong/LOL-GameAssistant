# 本机验证报告：2026-10-23

结论：用户态回归和模拟测试通过；驱动签名、内核动态测试及真实对局验收尚未完成。不能将本报告作为混合模式已通过完整验收的证明。

## 本轮实际结果

| 检查 | 结果 | 证据与边界 |
| --- | --- | --- |
| 换肤服务与镜头定向测试 | 62 项通过 | 本轮执行 `dotnet test`，不是游戏内测试 |
| 换肤页面、镜头 UI、设置保存测试 | 51 项通过 | 本轮执行 `dotnet test` |
| 混合主机、DLL、测试客户端 | 编译通过 | 本轮执行 `LeagueSkinHybrid/build.ps1 -Test` |
| 驱动协议布局与访问标志 | 通过 | 用户态 `--protocol-only` 测试 |
| 实际驱动分发函数的模拟测试 | 通过 | 覆盖所属句柄、目标身份、重复完成、取消、超时、配额与引用释放；不是内核环境 |
| 当前驱动签名状态 | `NotSigned` | `Get-AuthenticodeSignature` 检查 |
| 独立测试副本签名 | 未执行 | 创建临时开发证书并签署独立副本的命令被自动审批审核拒绝；仅返回 `blocked by policy` |
| 通道状态 | 不可用 | `driver-status` 返回 `kernel_driver_unavailable`，Win32 错误 2 |
| 真实内核加载、I/O 与卸载 | 未执行 | 没有已签名且可加载的测试驱动 |
| Driver Verifier / HVCI 动态验收 | 未执行 | 仅查询设置，未启用 Verifier |
| 英雄/小兵/眼位/特效真实对局验收 | 未执行 | 检查时只有 LeagueClient 相关进程，没有实际对局进程；驱动通道也不可用 |

## 本机前置检查

- Windows 11 专业版，版本 10.0.26100；当前进程具有提升后的管理员令牌。
- 已找到 Windows SDK 10.0.26100.0 的 x64 SignTool。
- CurrentUser/My 和 LocalMachine/My 中未发现可用的代码签名证书；这不代表用户没有其他外置证书或开发者账号。
- VBS 状态为运行，内存完整性对应的安全服务正在运行。
- Secure Boot 查询结果为关闭；当前启动配置未显示 TESTSIGNING 已开启。
- Driver Verifier 查询未配置受测驱动。
- 未发现可连接的已注册 Hyper-V 测试虚拟机或正在运行的常见 VM 进程。用户随后明确要求直接在本机测试。

## 操作边界与未完成原因

已尝试准备独立的测试签名副本，不打算覆盖原始 SYS 或安装信任证书。但整个签名命令在执行前被自动审批审核拦截，没有生成证书、PFX、测试签名 SYS 或签名结果文件。未通过其他工具重试或绕过该限制。

本轮没有安装驱动、改变证书信任、修改启动项、启用 Verifier、关闭系统保护或重启电脑。用户授权本机测试并不能改变当前工具对签名操作的执行限制。

继续内核动态验收需要已签名且满足该机加载策略的驱动，以及可加载和调试的测试环境；测试签名不等于正式发行签名。正式发行需相应微软驱动签名流程与用户的开发者资质，不能由生成本地自签名证书代替。

真实对局验收还需要实际对局与视觉观测，逐项核对普通皮肤、特殊模型/形态、技能、回城、小兵/眼位创建销毁、恢复和停用。历史其他核心的游戏验收记录不能替代本轮混合模式测试。

参考：[微软驱动签名选项](https://learn.microsoft.com/windows-hardware/drivers/dashboard/driver-signing-offerings)、[配置测试签名计算机](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/configuring-the-test-computer-to-support-test-signing)。
