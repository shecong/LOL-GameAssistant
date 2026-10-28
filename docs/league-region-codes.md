# 英雄联盟大区与编号对照

核对日期：2026-10-28。代码入口：`LOL-GameAssistant/Domain/LeagueClient/LeagueRegionNames.cs`。

本表汇总本次从公开资料核实的 LCU `platformId`、SGP 服务器标识及历史 region 别名，覆盖 **53 个展示名称、119 种编号写法**（不计大小写和首尾空格的变化）。历史编号保留用于识别旧客户端和休息室消息；列出编号不表示该服务器目前仍独立运营。

## 来源与核对方法

- [Riot 官方 LoL API 文档：Platform Routing Values](https://developer.riotgames.com/docs/lol#routing-values_platform-routing-values)：核对国际服 BR1、EUN1、EUW1、JP1、KR、LA1、LA2、NA1、OC1、TR1、RU、PH2、SG2、TH2、TW2、VN2。
- [League Akari：platform-names.ts，固定版本](https://github.com/LeagueAkari/LeagueAkari/blob/56cdfde00e56148f4641d1a8cf86acb66e903124/src/shared/utils/platform-names.ts)：核对国服旧区、五个合区、八个 `_NEW` 编号、国服 SGP 的 `TENCENT_` 前缀及亚洲服历史编号。
- [League Akari：sgp.ts，固定版本](https://github.com/LeagueAkari/LeagueAkari/blob/56cdfde00e56148f4641d1a8cf86acb66e903124/src/shared/utils/sgp.ts)：核对 NA、BR、TR、LAN、LAS、OCE、EUW、JP 等不同接口间的别名关系。该源明确规定 **LAN → LA1、LAS → LA2**；采用此关系，纠正同项目名称表中拉美南北名称颠倒的问题。
- [@fightmegg/riot-api：LoLRegion 与 TournamentV5.REGION](https://github.com/fightmegg/riot-api/blob/cff35b53eb7fd7378d8455197b5fa64b37932159/src/@types/index.ts)：补充核对 ME1、EUNE、PBE 等客户端/API 区域标识。
- [@fightmegg/riot-rate-limiter：PlatformId](https://github.com/fightmegg/riot-rate-limiter/blob/428f0b9743b9743aac2909b6509de1339ec2bff9/src/@types/index.ts)：交叉核对中东的实际路由值 `me1`，以及 enum 成员 `EUNE1` 的实际值 `eun1`，避免将程序内 enum 名误作服务器编号。

国服细分编号来自上述开源项目维护者发布的实际接口映射，并非 Riot 官方 API 的国际服枚举。未找到可靠对应关系的编号不据拼写猜测。

## 国服五个合区

| 编号 | 展示名称 | 包含的原大区（按 Akari 资料） |
| --- | --- | --- |
| NJ100 | 联盟一区 | 祖安、皮尔特沃夫、巨神峰、教育网专区、男爵领域、均衡教派、影流、守望之海 |
| GZ100 | 联盟二区 | 卡拉曼达、暗影岛、征服之海、诺克萨斯、战争学院、雷瑟守备 |
| CQ100 | 联盟三区 | 班德尔城、裁决之地、水晶之痕、钢铁烈阳、皮城警备 |
| TJ100 | 联盟四区 | 比尔吉沃特、弗雷尔卓德、扭曲丛林 |
| TJ101 | 联盟五区 | 德玛西亚、无畏先锋、恕瑞玛、巨龙之巢 |

合区编号只显示合区名称。例如 `NJ100` 能确定“联盟一区”，但不能区分其中的祖安、巨神峰等原大区。接口明确给出 `HN2` 时仍显示“祖安”，不反向猜测或改写账号身份。

## 国服原大区

| 编号 | 展示名称 |
| --- | --- |
| HN1 | 艾欧尼亚 |
| HN2 | 祖安 |
| HN3 | 诺克萨斯 |
| HN4 | 班德尔城 |
| HN5 | 皮尔特沃夫 |
| HN6 | 战争学院 |
| HN7 | 巨神峰 |
| HN8 | 雷瑟守备 |
| HN9 | 裁决之地 |
| HN10 | 黑色玫瑰 |
| HN11 | 暗影岛 |
| HN12 | 钢铁烈阳 |
| HN13 | 水晶之痕 |
| HN14 | 均衡教派 |
| HN15 | 影流 |
| HN16 | 守望之海 |
| HN17 | 征服之海 |
| HN18 | 卡拉曼达 |
| HN19 | 皮城警备 |
| WT1 | 比尔吉沃特 |
| WT2 | 德玛西亚 |
| WT3 | 弗雷尔卓德 |
| WT4 | 无畏先锋 |
| WT5 | 恕瑞玛 |
| WT6 | 扭曲丛林 |
| WT7 | 巨龙之巢 |
| EDU1 | 教育网专区 |
| BGP1 | 男爵领域 |
| BGP2 | 峡谷之巅 |

### 国服历史别名与 SGP 前缀

| 历史编号 | 对应编号 | 展示名称 |
| --- | --- | --- |
| HN4_NEW | HN4 | 班德尔城 |
| HN14_NEW | HN14 | 均衡教派 |
| HN16_NEW | HN16 | 守望之海 |
| HN18_NEW | HN18 | 卡拉曼达 |
| WT1_NEW | WT1 | 比尔吉沃特 |
| WT2_NEW | WT2 | 德玛西亚 |
| WT3_NEW | WT3 | 弗雷尔卓德 |
| WT4_NEW | WT4 | 无畏先锋 |

以上 29 个原大区、5 个合区和 8 个历史别名同时接受 `TENCENT_` 前缀，例如 `TENCENT_NJ100`、`TENCENT_HN14_NEW`。前缀只用于国服编号，不接受 `TENCENT_NA1` 之类未有来源的组合。

## 国际服、亚洲服与测试服

| 标准编号 | 展示名称 | 同时支持的别名 |
| --- | --- | --- |
| KR | 韩服 | — |
| NA1 | 北美 | NA |
| EUW1 | 欧洲西部 | EUW |
| EUN1 | 欧洲北部 | EUNE |
| JP1 | 日服 | JP |
| BR1 | 巴西 | BR |
| LA1 | 拉丁美洲北部 | LAN |
| LA2 | 拉丁美洲南部 | LAS |
| OC1 | 大洋洲 | OCE |
| RU | 俄罗斯 | RU1 |
| TR1 | 土耳其 | TR |
| ME1 | 中东 | — |
| PH2 | 菲律宾 | PH1 |
| SG2 | 新加坡 | SG1 |
| TH2 | 泰国 | TH1 |
| TW2 | 台湾 | TW1 |
| VN2 | 越南 | VN1 |
| PBE1 | 测试服（PBE） | PBE |
| TENCENT_PBE | 国服体验服 | — |

裸 `PBE` 无法单凭编号区分腾讯体验服与国际测试服，因此采用通用名称“测试服（PBE）”；明确带腾讯前缀时显示“国服体验服”。`PH1/SG1/TH1/TW1/VN1` 等历史代码保留原地区名称，用于兼容旧数据。

## 程序行为

- 忽略编号大小写及首尾空格。首页的大区查询与休息室共用本表，招募下拉列表使用去重后的完整名称列表。
- 成员信息、上线提示、状态栏以及旧战绩消息中的 `未知大区（NJ100）` 会按此表显示为“联盟一区”；用户普通聊天的原文不改写。
- `TENCENT`、`AMERICAS`、`ASIA`、`EUROPE`、`SEA` 等运营商/服务集群不代表一个具体登录大区，继续走未知大区处理。
- 本表处理 LCU/SGP 字符串编号。腾讯官网 `areaId`、登录器分区序号、Riot ID 的 `#标签` 属于不同的编号体系，不用于推断当前登录大区。
- 补充名称不会自动开放对应大区的跨区查询，也不会根据编号拼接未经核实的 SGP 服务地址。跨区查询仍使用现有已配置地址；CQ100 的选项名称修正为“联盟三区”。

## 验证

`LeagueRegionNamesTests` 覆盖合区名称、国际服清单、拉美南北关系、历史别名、未知编号、账号切换、旧战绩与成员展示、普通聊天原文保留和招募选项。

`HomeCurrentRegionTests` 同时验证五个合区与中东服可直接通过当前客户端的平台元数据识别，不依赖战绩或搜索对象。
