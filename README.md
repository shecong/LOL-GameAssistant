## **.net10制作LOL小助手**

[TOC]

### 功能架构

![架构图](architecture.html)

### 项目架构分层

| 层 | 职责 | 核心模块 |
|---|---|---|
| **表示层 UI** | 6 个 Tab 页，基于 AntdUI | GameMain · HomeForm · LiveGameForm · SettingForm · recordForm · InfoMsgForm |
| **API 层** | LCU REST 端点封装 | Game_Api · Assets_api · GetlolLcu · Select_Api |
| **基础设施层** | HTTP/WebSocket 通信、工具 | HttpClentHelper · WebSocketClient · StreamExtensions · ChampionMap(170+英雄) |
| **数据层** | 模型与解析器 | PlayerModel · GameDetailModel · GameHeadModel · LolRankedDataParser · GameFlowPhase |
| **外部依赖** | 本地 LCU + Riot CDN + NuGet | 15个LCU端点 · WebSocket实时事件 · Data Dragon · AntdUI · Newtonsoft.Json |

### 核心数据流

```
LeagueClientUx ──WMI──→ 获取认证(port+token)
     │
     ├── REST(HTTPS) ──→ HttpClentHelper ──→ Stream ──→ 模型 ──→ UI 绑定
     │
     └── WebSocket ──→ WebSocketClient ──→ OnJsonApiEvent ──→ 游戏状态自动响应
         Lobby(大厅)→自动匹配    ReadyCheck→自动接受    ChampSelect/InProgress→刷新对局
```

界面效果图

![](https://s3.bmp.ovh/imgs/2025/09/26/7559ecc4e047ff15.png)

  目前的话，个人信息和排位信息，以及战绩界面都已经初步完善,后续功能还在陆续增加中

