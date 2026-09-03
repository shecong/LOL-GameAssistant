
namespace LOL_GameAssistant.Entity
{
    /// <summary>
    /// 对局详情模型扩展方法，统一处理“按 puuid 查找玩家/胜负/KDA”等重复逻辑。
    /// </summary>
    public static class GameDetailExtensions
    {
        /// <summary>
        /// 按 puuid 获取对局中的玩家身份信息。
        /// </summary>
        public static GameDetailModel.Player? GetPlayerIdentity(this GameDetailModel.GameInfo game, string? puuid)
        {
            if (game == null || string.IsNullOrEmpty(puuid)) return null;
            return game.participantIdentities?
                .FirstOrDefault(p => p?.player?.puuid == puuid)?
                .player;
        }

        /// <summary>
        /// 按 puuid 获取对局中的玩家参赛数据。
        /// </summary>
        public static GameDetailModel.ParticipantsItem? GetParticipant(this GameDetailModel.GameInfo game, string? puuid)
        {
            if (game == null || string.IsNullOrEmpty(puuid)) return null;
            var identity = game.participantIdentities?.FirstOrDefault(p => p?.player?.puuid == puuid);
            if (identity == null) return null;
            return game.participants?.FirstOrDefault(p => p.participantId == identity.participantId);
        }

        /// <summary>
        /// 判断参赛玩家本场是否胜利。兼容 LCU 返回的 bool、字符串和数字格式。
        /// </summary>
        public static bool IsWin(this GameDetailModel.ParticipantsItem? participant)
        {
            object? rawWin = participant?.stats?.win;
            if (rawWin is bool boolWin) return boolWin;

            string value = Convert.ToString(rawWin)?.Trim() ?? "";
            return value.ToLowerInvariant() switch
            {
                "true" or "1" or "win" or "won" or "victory" or "success" or "胜" or "胜利" or "赢" => true,
                _ => false
            };
        }

        /// <summary>
        /// 获取 KDA 文本（击杀/死亡/助攻）。
        /// </summary>
        public static string GetKdaText(this GameDetailModel.ParticipantsItem? participant)
            => participant?.stats == null
                ? "0/0/0"
                : $"{participant.stats.kills}/{participant.stats.deaths}/{participant.stats.assists}";

        /// <summary>
        /// 计算 KDA 比值（死亡为 0 时按击杀+助攻计算）。
        /// </summary>
        public static double GetKdaRatio(this GameDetailModel.ParticipantsItem? participant)
        {
            if (participant?.stats == null) return 0;
            var s = participant.stats;
            return s.deaths > 0
                ? Math.Round((double)(s.kills + s.assists) / s.deaths, 2)
                : s.kills + s.assists;
        }

        /// <summary>
        /// 获取对局时长文本（分:秒）。
        /// </summary>
        public static string GetDurationText(this GameDetailModel.GameInfo? game)
        {
            int seconds = Math.Max(0, game?.gameDuration ?? 0);
            return $"{seconds / 60}:{seconds % 60:D2}";
        }

        /// <summary>
        /// 获取用户可读的队列模式，优先根据队列 ID 区分具体玩法，
        /// 未知队列再回退到 LCU 返回的 gameMode。
        /// </summary>
        private static string GetQueueModeText(GameDetailModel.GameInfo? game)
        {
            if (game == null) return "未知模式";

            string queueId = (game.queueId ?? game._queueId ?? "").Trim();
            if (int.TryParse(queueId, out int id))
            {
                string? queueName = id switch
                {
                    0 => "自定义对局",
                    2 or 14 or 400 => "匹配（征召）",
                    4 or 6 or 41 or 42 or 410 or 420 => "单双排",
                    7 or 31 or 32 or 33 or 52 or 53 or 61 or 68 or 83 or 830 or 840 or 850 => "人机对战",
                    16 or 17 or 25 => "统治战场",
                    65 or 67 or 450 => "极地大乱斗",
                    70 or 1020 => "克隆大作战",
                    76 or 900 or 1900 => "无限火力",
                    100 => "飞升模式",
                    1300 => "极限闪击",
                    1400 => "终极魔典",
                    1700 or 1710 or 1810 or 1820 or 1830 or 1840 => "斗魂竞技场",
                    430 => "匹配（盲选）",
                    440 => "灵活组排",
                    490 => "快速游戏",
                    600 => "血月杀",
                    610 => "黑市争夺战",
                    700 => "冠军杯赛",
                    1090 => "云顶之弈",
                    1100 => "云顶之弈排位",
                    1110 => "云顶之弈教程",
                    _ => null
                };

                if (!string.IsNullOrEmpty(queueName)) return queueName;
            }
            else
            {
                string queueName = queueId.ToUpperInvariant() switch
                {
                    "RANKED_SOLO_5X5" => "单双排",
                    "RANKED_FLEX_SR" => "灵活组排",
                    "ARAM_UNRANKED_5X5" => "极地大乱斗",
                    "NORMAL" => "匹配",
                    _ => ""
                };
                if (!string.IsNullOrEmpty(queueName)) return queueName;
            }

            return (game.gameMode ?? "").Trim().ToUpperInvariant() switch
            {
                "CLASSIC" => "经典模式",
                "ARAM" => "极地大乱斗",
                "CHERRY" => "斗魂竞技场",
                "URF" => "无限火力",
                "NEXUS_BLITZ" => "极限闪击",
                "ULTBOOK" => "终极魔典",
                "TFT" => "云顶之弈",
                "TUTORIAL" => "新手教程",
                _ when !string.IsNullOrEmpty(queueId) => $"队列 {queueId}",
                _ => "其他模式"
            };
        }

        /// <summary>
        /// 获取用户可读的对局模式和地图名称。
        /// </summary>
        public static string GetModeText(this GameDetailModel.GameInfo? game)
        {
            if (game == null) return "未知模式";

            string mode = GetQueueModeText(game);
            string map = game.GetMapModeText();
            return string.IsNullOrEmpty(map) || string.Equals(mode, map, StringComparison.Ordinal)
                ? mode
                : $"{mode} · {map}";
        }

        /// <summary>
        /// 获取对局地图名称。
        /// </summary>
        public static string GetMapModeText(this GameDetailModel.GameInfo? game)
        {
            if (game == null) return "";

            string? mapName = game.mapId switch
            {
                1 or 2 or 11 => "召唤师峡谷",
                3 => "试炼之地",
                4 or 10 => "扭曲丛林",
                8 => "水晶之痕",
                12 or 14 => "嚎哭深渊",
                16 => "星之守护者",
                18 => "极限闪击",
                22 => "云顶之弈",
                30 => "斗魂竞技场",
                _ => null
            };

            if (!string.IsNullOrEmpty(mapName)) return mapName;

            return (game.gameMode ?? "").Trim().ToUpperInvariant() switch
            {
                "CLASSIC" => "召唤师峡谷",
                "ARAM" => "嚎哭深渊",
                "CHERRY" => "斗魂竞技场",
                "NEXUS_BLITZ" => "极限闪击",
                "TFT" => "云顶之弈",
                _ => ""
            };
        }
    }
}
