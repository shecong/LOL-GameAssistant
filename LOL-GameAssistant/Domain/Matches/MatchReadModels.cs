namespace LOL_GameAssistant.Domain.Matches;

/// <summary>战绩列表的领域读模型。</summary>
public sealed class MatchHistoryResponse
{
    public MatchHistoryGames? Games { get; set; }
}

/// <summary>战绩分页结果与比赛条目。</summary>
public sealed class MatchHistoryGames
{
    public int GameCount { get; set; }
    public int GameIndexBegin { get; set; }
    public int GameIndexEnd { get; set; }
    public List<MatchHistoryGame> Games { get; set; } = new();
}

/// <summary>列表页展示所需的单场比赛摘要。</summary>
public sealed class MatchHistoryGame
{
    public long GameId { get; set; }
    public long GameCreation { get; set; }
    public string GameMode { get; set; } = "";
    public int QueueId { get; set; }
    public List<MatchIdentity> ParticipantIdentities { get; set; } = new();
}

/// <summary>战绩摘要中的参赛者身份。</summary>
public sealed class MatchIdentity
{
    public int ParticipantId { get; set; }
    public MatchPlayer? Player { get; set; }
}

/// <summary>单局比赛的领域读模型。</summary>
public sealed class MatchDetail
{
    public long GameId { get; set; }
    public string? gameCreationDate { get; set; }
    public int gameDuration { get; set; }
    public string gameMode { get; set; } = "";
    public string queueId { get; set; } = "";
    public string _queueId { get; set; } = "";
    public List<MatchParticipantIdentity> participantIdentities { get; set; } = new();
    public List<MatchParticipant> participants { get; set; } = new();
}

/// <summary>单局内的玩家身份。</summary>
public sealed class MatchParticipantIdentity
{
    public int participantId { get; set; }
    public MatchPlayer? player { get; set; }
}

/// <summary>对局中的召唤师标识。</summary>
public sealed class MatchPlayer
{
    public string Puuid { get; set; } = "";
    public string GameName { get; set; } = "";
    public string SummonerName { get; set; } = "";
    public string TagLine { get; set; } = "";

    // 单局详情的既有 UI 使用 LCU 小写字段；在读模型内保留同义访问器，
    // 避免展示层重新了解 LCU 字段差异。
    public string puuid { get => Puuid; set => Puuid = value ?? ""; }
    public string gameName { get => GameName; set => GameName = value ?? ""; }
    public string summonerName { get => SummonerName; set => SummonerName = value ?? ""; }
}

/// <summary>单局中一名玩家的英雄、召唤师技能与统计。</summary>
public sealed class MatchParticipant
{
    public int championId { get; set; }
    public int participantId { get; set; }
    public int Spell1Id { get; set; }
    public int Spell2Id { get; set; }
    public int teamId { get; set; }
    public MatchParticipantStats? stats { get; set; }
}

/// <summary>本项目战绩展示和评测所需的稳定统计字段。</summary>
public sealed class MatchParticipantStats
{
    public int assists { get; set; }
    public int champLevel { get; set; }
    public int deaths { get; set; }
    public int doubleKills { get; set; }
    public int goldEarned { get; set; }
    public int item0 { get; set; }
    public int item1 { get; set; }
    public int item2 { get; set; }
    public int item3 { get; set; }
    public int item4 { get; set; }
    public int item5 { get; set; }
    public int item6 { get; set; }
    public int kills { get; set; }
    public int neutralMinionsKilled { get; set; }
    public int pentaKills { get; set; }
    public int quadraKills { get; set; }
    public int totalDamageDealtToChampions { get; set; }
    public int totalDamageTaken { get; set; }
    public int totalHeal { get; set; }
    public int totalMinionsKilled { get; set; }
    public int totalTimeCrowdControlDealt { get; set; }
    public int tripleKills { get; set; }
    public int visionScore { get; set; }
    public bool Win { get; set; }
}

/// <summary>战绩读模型的业务计算与模式名称规则。</summary>
public static class MatchDetailExtensions
{
    public static MatchPlayer? GetPlayerIdentity(this MatchDetail game, string? puuid) =>
        string.IsNullOrWhiteSpace(puuid)
            ? null
            : game.participantIdentities.FirstOrDefault(item => item.player?.puuid == puuid)?.player;

    public static MatchParticipant? GetParticipant(this MatchDetail game, string? puuid)
    {
        if (string.IsNullOrWhiteSpace(puuid)) return null;
        int? participantId = game.participantIdentities
            .FirstOrDefault(item => item.player?.puuid == puuid)?.participantId;
        return participantId is null
            ? null
            : game.participants.FirstOrDefault(item => item.participantId == participantId.Value);
    }

    public static bool IsWin(this MatchParticipant? participant) => participant?.stats?.Win == true;

    public static string GetKdaText(this MatchParticipant? participant) => participant?.stats is { } stats
        ? $"{stats.kills}/{stats.deaths}/{stats.assists}"
        : "0/0/0";

    public static double GetKdaRatio(this MatchParticipant? participant)
    {
        if (participant?.stats is not { } stats) return 0;
        return stats.deaths > 0
            ? Math.Round((double)(stats.kills + stats.assists) / stats.deaths, 2)
            : stats.kills + stats.assists;
    }

    public static string GetDurationText(this MatchDetail? game)
    {
        int seconds = Math.Max(0, game?.gameDuration ?? 0);
        return $"{seconds / 60}:{seconds % 60:D2}";
    }

    /// <summary>对局记录只显示玩法模式名称，不使用地图名作为模式名。</summary>
    public static string GetModeText(this MatchDetail? game)
    {
        if (game == null) return "未知模式";
        string queueId = (game.queueId ?? game._queueId ?? "").Trim();
        if (int.TryParse(queueId, out int id))
        {
            string? name = id switch
            {
                0 => "自定义对局",
                2 or 14 or 400 => "峡谷匹配",
                4 or 6 or 41 or 42 or 410 or 420 => "峡谷单双排",
                7 or 31 or 32 or 33 or 52 or 53 or 61 or 68 or 83 or 830 or 840 or 850 => "人机对战",
                65 or 67 or 450 => "深渊大乱斗",
                70 or 1020 => "克隆大作战",
                76 or 900 or 1900 => "无限火力",
                1300 => "极限闪击",
                1400 => "终极魔典",
                1700 or 1710 or 1810 or 1820 or 1830 or 1840 => "斗魂竞技场",
                430 => "峡谷匹配（盲选）",
                440 => "峡谷灵活组排",
                490 => "峡谷快速游戏",
                _ => null
            };
            if (!string.IsNullOrEmpty(name)) return name;
        }

        return game.gameMode.Trim().ToUpperInvariant() switch
        {
            "CLASSIC" => "峡谷对局",
            "ARAM" => "深渊大乱斗",
            "CHERRY" => "斗魂竞技场",
            "URF" => "无限火力",
            "NEXUS_BLITZ" => "极限闪击",
            "ULTBOOK" => "终极魔典",
            "TFT" => "云顶之弈",
            _ when !string.IsNullOrEmpty(queueId) => $"队列 {queueId}",
            _ => "其他模式"
        };
    }
}
