# 原版1皮肤核心更新 · 2026-10-29

已根据用户登录后的原版1最新模块适配本机 16.21.832.5350，项目标准核心及混合核心均重新编译并同步到 Tools。没有把原版 DLL、授权 SDK 或凭据分发到助手。

## 来源与解析

原版前端 `E:\tools\lol\lol小助手.exe` 哈希仍为 `15096d74df2293f047235a5873d7d61929fddbccc958739dac565d1c053dc487`。最新授权下载的 `R3nzSkin.dll` 为 757,872 字节，SHA256 `cf4ed878813631efaf4018ad7cfa8354ab10b3b395a29ca64411e13590a6d3b2`，已与 10 月 9 日的旧模块分目录保存。

新模块增加 `HookProc`、`NextHook` 兼容导出，增加玩家及直接编码路径的定位规则。主体仍是写入玩家编码皮肤编号与 CharacterDataStack 基础编号、处理特殊 gear 或 Lux/Sona 层栈，再调用游戏刷新函数。不能把 DLL 内部 RVA 当作游戏 RVA。

最新特殊规则函数为 DLL RVA `0x562e0..0x5667c`。默认 gear 新增或修正 Ezreal 5、Leona 64、Caitlyn 22、Zeri 40、Thresh 19、Lillia 46、Aatrox 33、Evelynn 24、Tristana 80；保留原有 Katarina、Jax、Renekton 等范围判断。新版 Ashe 与 MasterYi 走默认重置分支；Kayn 保留原 gear，Lux/Sona 的层栈路径保留由原版函数确认的 gear 行为。用户明确选择已有 gear 条目时仍按选择值处理。

新模块同时保留旧 XOR 容器编码和直接 DWORD 编码分支。本次游戏实码确认适用直接编码：两次循环移位与 XOR 常量 `0x3dc9cb21` 未变，因此没有启用未经当前布局验证的旧容器写入。

## 当前游戏适配

普通跨进程读仍被拒绝，改用现有 Windows 消息钩子的独立只读探测构建；探测仅接受状态及可执行节分页读取，使用不同消息和共享内存命名，与生产核心隔离。没有关闭保护、安装驱动或新增读写驱动。取得 25,361,688 字节完整可执行节，并核对同一 PID、时间戳和映像大小。

|项目|新适配|
|---|---|
|时间戳 / 映像大小|`0x6adff306` / `0x1f0b000`|
|玩家 / 英雄数据库槽|`0x1cba718` / `0x1c7aeb8`|
|对象列表槽|`0x1c7e430`|
|翻译 / Update / Push|`0xf71e10` / `0x21c020` / `0x238040`|
|皮肤栈 / 编码编号 / 拥有者|`+0x3638` / `+0xbe4` / `+0x560`|
|配置指纹标识|`80bff3d1cdedad4de07690958b17af85bd4b84ea23efa2e6266c45f1931b17e0`|

13 处公共指纹均由新代码生成，保留完整版本拒绝和结构检查。相机四处独立指纹及两个全局槽也重新定位，原生内存模拟测试覆盖其更新。旧配置保存为 `profiles/2026-10-22.json`。

## 验证及限制

- 当前练习模式 Heimerdinger：读取 27 条皮肤，0 → 1 → 独立读取 1 → 恢复 0 → 独立读取 0，通过，当前已恢复原皮。
- 当前练习模式小兵：49 个目标应用主题1及恢复原编号，通过；没有改眼位、野怪或其他英雄。
- 眼位目录读取 266 条，相机当前上限读取 600，通过；本次没有放眼测试或实际镜头写入。
- 原生测试通过，新增特殊规则及边界覆盖；相关服务测试 87 项、相关 UI 测试 45 项通过，Release 构建 0 警告、0 错误。
- 混合核心编译及用户态协议/分发测试通过，没有加载驱动，未声称混合模式运行时通过。
- 状态读回不代表画面验收；其他英雄特殊形态、眼位、技能特效及其他地图尚未逐项游戏验收。

游戏记录：`original1-verification-2026-10-29.json`、`original1-minions-2026-10-29.json`、`original1-catalog-2026-10-29.json`、`original1-objects-2026-10-29.json`、`original1-camera-2026-10-29.json`。

解析材料位于 `E:\tools\lol\output\lol-assistant-analysis\downloaded-dlls-2026-10-29`，只读代码及匹配结果位于 `E:\tools\lol\output\skin-core-update-2026-10-29`。

工具：`tools/prepare-original1-update-probe.py`、`tools/capture-skin-update-via-bridge.py`、`tools/adapt-original1-core-profile.py`。这些工具用于此次已确认样本及代码快照，不自动承诺未来游戏版本兼容。
