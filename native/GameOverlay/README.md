# 海克斯增幅游戏覆盖层（DX11 / x64）

增幅侧栏改由游戏的 DirectX 11 `Present` 路径绘制，不再在对局中显示、激活或反复置顶 WinForms 窗口。现有 WinForms 对象仅保存界面状态、运行推荐/OCR 调度。

## 使用

保持设置中的“海克斯大乱斗显示局内增幅侧边栏”开启，进入海克斯对局。覆盖层接入后自动显示推荐。鼠标移入面板即可点击扫描、切换白银/黄金/棱彩、收起/展开和滚动列表，无需按 Alt。收起后仅保留贴右侧边缘的 48×48 小入口，保留上下位置，支持点击展开与沿右侧上下拖动；展开面板点击整个标题导航栏均可收起，拖动标题栏可移动面板。只有从交互面板内按下的鼠标点击由覆盖层处理，面板外继续操作游戏。

对局详情、AI 时间线建议、皮肤选择器、换肤成功提示也统一使用同一原生覆盖层。只读建议和对局详情不拦截鼠标；皮肤选择器直接点击，保留读取/恢复/隐藏操作。各面板独立开关，由后台心跳维持连接，不依赖海克斯模式启用。

推荐识别直接读取游戏后缓冲区中央卡片区域，在覆盖层绘制前取图，不需要切桌面，不会截到自己的推荐文字。支持 RGBA8/BGRA8（含 sRGB）及多重采样；HDR/10-bit 格式明确报错。

## 构建

需要 Windows x64、Visual Studio C++ Build Tools、Windows SDK、Git 和 .NET 10。

```powershell
powershell -ExecutionPolicy Bypass -File native/GameOverlay/build.ps1 -Test
dotnet test LOL-GameAssistant.UiTests/LOL-GameAssistant.UiTests.csproj --filter "FullyQualifiedName~MayhemFullscreenTests|FullyQualifiedName~GameOverlayProtocolTests"
dotnet publish LOL-GameAssistant/LOL-GameGUI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o output/SC-game-overlay-ui-v2
```

构建脚本检查并下载固定修订的依赖，构建原生 DLL/加载器，将发布需要的文件与许可复制到 `LOL-GameAssistant/Tools/GameOverlay/`。普通托管构建使用已有原生文件；需要同时构建时可传 `-p:BuildGameOverlay=true`。发布缺少原生文件会报错，避免产生没有覆盖层的安装包。

依赖（未经修改）：

- Dear ImGui v1.91.9b，`f5befd2d29e66809cd1110a152e375a7f1981f06`，MIT，<https://github.com/ocornut/imgui>
- MinHook v1.3.4，`c3fcafdc10146beb5919319d0683e44e3c30d537`，BSD 2-Clause 及其依赖许可，<https://github.com/TsudaKageyu/minhook>

## 验证范围与限制

`build.ps1 -Test` 使用独立 DX11 程序验证真实绘制、游戏渲染目标恢复、绘制前取图、RGBA→BGRA、停用、心跳过期、48×48 收起、无需 Alt 的真实鼠标点击、独立面板与带版本的皮肤选择指令、换肤列表双向滚动、消息队列提前拦截、原始滚轮过滤，以及移出/关闭面板后恢复游戏滚轮、窗口分辨率变化。运行以下命令可额外验证独占全屏，会短暂显示测试窗口并在结束后恢复桌面：

```powershell
cd native/GameOverlay/bin
./sc-game-overlay-smoke.exe --exclusive
```

测试 DLL 和测试程序不进入发布目录。测试通过证明渲染与传输机制可运行，不等于已验证受保护游戏内的加载与输入兼容性。

生产加载器严格检查目标为 `League of Legends.exe`，仅通过标准 `SetWindowsHookEx(WH_GETMESSAGE)` 引导原生模块，在游戏内用 MinHook 接入 DX11 Present。不会禁用或绕过游戏保护机制。加载失败本局不再自动尝试；助手“运行诊断”的“游戏覆盖层”记录加载器退出码和原生状态，日志位于 `%LOCALAPPDATA%/LOL-GameAssistant/diagnostics.log`。状态 1 表示已接入、2 表示真正绘制过，负数表示失败；仅接入而无提交帧的场景不能算作成功。

本模块不是腾讯/WeGame 官方集成，受保护环境可能拒绝加载。未实现 DX9/DX12/Vulkan；不承诺所有游戏版本兼容。鼠标悬停在交互面板内时，垂直滚轮在游戏读取消息队列前交给覆盖层，同时过滤 `GetRawInputData` / `GetRawInputBuffer` 返回的垂直滚轮标志，避免换肤列表滚动同时缩放游戏视距；原始鼠标位移、按键及水平滚轮保持原值。移出面板、关闭面板或失去焦点后恢复游戏滚轮；其他输入路径仍需游戏内实测。

## 生命周期与协议

每局按游戏 PID、助手 PID 和随机 nonce 创建同会话共享内存及互斥量。协议定义在 `protocol.h`，托管侧对应 `NativeGameOverlayBridge`。文本使用有界 UTF-8，截断不拆分字符；v2 协议分开维护增幅状态、其他面板标志、时限、带版本的皮肤列表/指令，以及最大 1600×1000 的对局详情位图。命令通道支持扫描、切换稀有度和收起/展开；辅助命令槽独立于主命令槽，不在更新文字时清空。取图有独立请求/响应序号和 32 MiB 有界像素区。

运行时先将加载器和 DLL 按完整内容的 SHA-256 组合版本复制到 `%LOCALAPPDATA%/LOL-GameAssistant/GameOverlay/runtime/<hash>/`，只从该缓存启动与加载；不直接加载 `bin` 或发布目录的文件。同版本复用路径，新版本使用独立目录，缓存写入采用临时文件和原子移动，并在运行前校验内容。这样游戏持有原生模块时也不会阻止再次构建、发布或更新安装目录。

真正退出助手（关闭到托盘除外）会立即关闭共享渲染开关、停止心跳、释放托管共享内存，并取消/结束尚未退出的加载器。游戏内已固定加载的代码仍保留至游戏结束，以免线程调用已卸载代码；旧版从发布目录直接加载的 DLL，需要退出那一局游戏后才能解除原发布路径的占用。

帧钩子不会等待助手锁，锁忙时复用尚未过期的最近快照。关闭某个面板不会影响其他面板；助手异常退出或停止更新后 5 秒内停止绘制。为防止渲染线程调用已卸载代码，加载后的模块留驻到游戏进程退出，不尝试运行中卸载。重新开启功能复用同局模块；跨助手进程重连仅允许原会话已过期或停用的情况。

更新原生模块后需退出并重开游戏，再启动新版助手；旧游戏进程中已固定加载的 DLL 无法由替换文件热更新。
