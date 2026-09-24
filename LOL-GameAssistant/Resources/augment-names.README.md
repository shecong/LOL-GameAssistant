# 强化中文名数据

`augment-names.json` 在 2026-09-24 生成，仅保存强化 ID、中文名和可用的图标地址。程序把它嵌入单文件发布包，战绩页离线时仍能将海克斯大乱斗的强化 ID 显示为中文名。

- 海克斯大乱斗：<https://hextech.dtodo.cn/data/aram-mayhem-augments.zh_cn.json> 的 `id` / `displayName`。
- 斗魂竞技场：<https://raw.communitydragon.org/latest/cdragon/arena/zh_cn.json> 的 `id` / `name` / `iconSmall`。

新赛季出现未知 ID 时，更新这个映射文件即可；界面会对未知 ID 显示编号。
