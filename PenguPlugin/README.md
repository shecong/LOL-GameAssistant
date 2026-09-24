# LOL GameAssistant Visuals（Pengu 插件层）

这是外置 C# 助手的可选客户端内视觉层。它只在 Pengu Loader 注入的 League Chromium 窗口内运行，不负责自动接受、选人、奖励或任何 LCU 写操作。

## 提供的视觉功能

- 右下角 `✦` 视觉面板：保存本地壁纸 URL、菜单隐藏和角标开关；
- 选人网格、英雄卡片、备战席和队伍英雄头像上的 OP.GG T 级角标；按客户端 DOM 中已有的英雄 ID 实时挂载；
- 背景壁纸覆盖和导航栏隐藏；关闭开关或清空 URL 后立即恢复。

## 安装

1. 在 Pengu Loader 的插件目录中创建 `lol-game-assistant-visuals` 文件夹；
2. 将本目录中的 `pengu.yml` 和 `index.js` 复制进去；
3. 重启 League 客户端，点击右下角 `✦` 配置视觉项。

该插件使用浏览器 `localStorage` 保存视觉偏好。League 客户端页面结构会随版本变化；脚本使用保守的 DOM 选择器，若某版本未显示角标，其余视觉功能不受影响。
