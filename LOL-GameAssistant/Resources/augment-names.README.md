# 强化中文名数据

`augment-names.json` 在 2026-09-24 生成，仅保存强化 ID、中文名和可用的图标地址。程序把它嵌入单文件发布包，战绩页离线时仍能将海克斯大乱斗的强化 ID 显示为中文名。

- 海克斯大乱斗：<https://hextech.dtodo.cn/data/aram-mayhem-augments.zh_cn.json> 的 `id` / `displayName`。
- 斗魂竞技场：<https://raw.communitydragon.org/latest/cdragon/arena/zh_cn.json> 的 `id` / `name` / `iconSmall`。

新赛季出现未知 ID 时，更新这个映射文件即可；界面会对未知 ID 显示编号。

`augment-descriptions.json` 在 2026-10-06 从上述海克斯大乱斗数据源的 `description` 字段生成，保存 247 条中文作用说明，并嵌入发布包。优先使用说明字段，避免将需要局内动态参数的 tooltip 当作确定数值显示。详情窗口将游戏富文本标签转换为普通文本；新版本更新名称时也应同步更新说明文件。
