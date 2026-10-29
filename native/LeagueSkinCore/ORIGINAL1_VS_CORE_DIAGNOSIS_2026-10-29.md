# 原版1与编译核心差异核验 · 2026-10-29

## 已复现的直接原因

助手 `events.jsonl` 中失败请求使用的路径是：

`E:\code\weather-app\LOL-GameAssistant\bin\Release\net10.0-windows7.0\publish\win-x64\Tools\SkinCore\league-skin-core-host.exe`

这份发布包的 host/DLL 文件修改时间仍为 2026-10-26 13:50，未包含本次新版游戏适配。上次更新同步了源码、项目 Tools 和新的 Release 输出目录，但遗漏了这份已有发布包。

在同一练习局、同一 PID 48548 读取状态：

|调用来源|修复前结果|
|---|---|
|native/LeagueSkinCore/bin|成功；新配置 `80bff3d1…b17e0`|
|Release/net10.0-windows10.0.19041.0/Tools/SkinCore|成功；与 native 构建文件哈希一致|
|Release/net10.0-windows7.0/publish/win-x64/Tools/SkinCore|`independent_game_version_not_supported`，`stage=validation`，`invoked=false`|

因此该发布包在版本验证阶段已经退出，没有进入皮肤修改调用。

|文件|旧发布包 SHA256|最新构建 SHA256|
|---|---|---|
|host|`f9903147157dce05676218f6e4eb5156453b7e379087a7a852021c6519fe3111`|`136b83278f646386299aa80beb26601fae9bcd552eaf53928ee86cbfba3f4192`|
|DLL|`360283b450f575393034e21609d380c9e7150f8d6b6eb2069646678fe56d3f55`|`dbec3d408a336b30adcc3122d42426b329cce0c44e2fceaed62b5dd2a12e5b5a`|

## 已完成修复与验证

备份旧发布包的 host、DLL、校验文件后，将这三个文件替换为已验证的新构建。新发布目录校验文件与两个二进制逐一匹配。

通过**修复后的原发布路径**完成：大头原皮 0 → 皮肤 1 → 等待 1.5 秒后独立读回 1 → 恢复 0 → 独立读回 0。应用、恢复均 `state_verified=true`，当前已恢复原皮。此结果确认发布路径和加载模块已经更新，不代表画面已由用户验收。

备份及原始记录：`.artifacts/core-runtime-repair-2026-10-29/repair.json`、`before-eb6bfe7d/`、`published-core-verification.json`。

## 实现层面的差异

|项目|原版1最新模块|独立编译核心|
|---|---|---|
|版本来源|前端通过已登录 SDK 下载 R3nzSkin.dll|使用随助手分发的本地 host/DLL，不自动向原版服务器更新|
|定位方式|包含签名扫描及兼容分支|本次从代码确认地址，保留固定版本、13 处指纹及结构检查；旧包会拒绝新版|
|编号编码|保留旧 XOR 容器与直接 DWORD 编码分支|当前仅启用已由本版实码验证的 DWORD 编码|
|加载方式|前端存在多种驱动/兼容加载入口，最新模块有 HookProc/NextHook 导出|标准模式用 Windows 消息钩子；混合模式需要本项目自己的 relay 驱动|
|生命周期|功能模块在游戏中持续运行|标准请求完成后释放消息钩子；部分特殊模型固定模块以维持字符串寿命|
|范围|原版有英雄、其他对象和菜单处理循环|独立核心按请求处理当前英雄；对象自动同步由助手服务调度|

本机混合驱动状态查询返回 `kernel_driver_unavailable`、Win32 2；保存的选择为标准模式（SkinCoreMode=0），因此它不是本次标准模式版本校验失败的原因。核验时保存的功能总开关为关闭，未擅自修改用户设置；在助手界面使用时仍需启用。

两种产品并不是逐字节相同的模块。此次确认的故障是发布文件未同步，而不是依据这些架构差异推测某一种注入或刷新方法必然失败。
