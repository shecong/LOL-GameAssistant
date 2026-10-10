using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Entity;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>
/// 旧 LCU 战绩 DTO 到领域读模型的集中映射。
/// 历史字段大小写和 Win 多类型兼容均停留在基础设施层。
/// </summary>
internal static class LegacyMatchReadModelMapper
{
    /// <summary>某些客户端忽略分页参数，按返回索引裁出请求区间，避免创建整页历史控件。</summary>
    internal static MatchHistoryResponse? ToDomainPage(GameHeadModel.MatchHistoryResponse? source,
        int beginIndex, int endIndex)
    {
        if (beginIndex < 0 || endIndex < beginIndex) throw new ArgumentOutOfRangeException(nameof(beginIndex));
        MatchHistoryResponse? result = ToDomain(source);
        if (result?.Games == null) return result;
        var page = result.Games;
        int requestedCount = endIndex - beginIndex + 1;
        bool hasRange = page.GameIndexEnd > 0 || beginIndex == 0 || page.Games.Count > requestedCount;
        int returnedBegin = hasRange ? page.GameIndexBegin : beginIndex;
        // Never present a different range as the requested page when the server
        // cannot supply it. GameCount remains the server's total, not this slice.
        page.Games = beginIndex < returnedBegin ? [] : page.Games
            .Skip(beginIndex - returnedBegin).Take(requestedCount).ToList();
        page.GameIndexBegin = beginIndex;
        page.GameIndexEnd = beginIndex + page.Games.Count - 1;
        return result;
    }

    /// <summary>将旧接口或持久化数据映射为业务层使用的领域对象。</summary>
    public static MatchHistoryResponse? ToDomain(GameHeadModel.MatchHistoryResponse? source)
    {
        if (source == null) return null;
        return new MatchHistoryResponse
        {
            Games = source.Games == null ? null : new MatchHistoryGames
            {
                GameCount = source.Games.GameCount,
                GameIndexBegin = source.Games.GameIndexBegin,
                GameIndexEnd = source.Games.GameIndexEnd,
                Games = (source.Games.Games ?? new List<GameHeadModel.GameInfo>())
                    .Select(item => new MatchHistoryGame
                    {
                        GameId = item.GameId,
                        GameCreation = item.GameCreation,
                        GameMode = item.GameMode ?? "",
                        QueueId = item.QueueId,
                        GameDuration = item.GameDuration,
                        EndOfGameResult = item.EndOfGameResult ?? "",
                        ParticipantIdentities = (item.ParticipantIdentities ?? new List<GameHeadModel.ParticipantIdentity>())
                            .Select(identity => new MatchIdentity
                            {
                                ParticipantId = identity.ParticipantId,
                                Player = identity.Player == null ? null : new MatchPlayer
                                {
                                    Puuid = identity.Player.Puuid ?? "",
                                    GameName = identity.Player.GameName ?? "",
                                    SummonerName = identity.Player.SummonerName ?? "",
                                    TagLine = identity.Player.TagLine ?? ""
                                }
                            })
                            .ToList(),
                        // 列表条目只带回被查询玩家本人的参赛数据，但已包含 KDA 与胜负，
                        // 近期评分因此不必再逐场拉详情。
                        Participants = (item.Participants ?? new List<GameHeadModel.Participant>())
                            .Select(participant => new MatchParticipant
                            {
                                championId = participant.ChampionId,
                                participantId = participant.ParticipantId,
                                Spell1Id = participant.Spell1Id,
                                Spell2Id = participant.Spell2Id,
                                teamId = participant.TeamId,
                                stats = ToDomain(participant.Stats)
                            })
                            .ToList()
                    })
                    .ToList()
            }
        };
    }

    /// <summary>将旧接口或持久化数据映射为业务层使用的领域对象。</summary>
    public static MatchDetail? ToDomain(GameDetailModel.GameInfo? source)
    {
        if (source == null) return null;
        return new MatchDetail
        {
            GameId = source.gameId,
            gameCreationDate = source.gameCreationDate,
            gameDuration = source.gameDuration,
            gameMode = source.gameMode ?? "",
            queueId = source.queueId ?? "",
            _queueId = source._queueId ?? "",
            participantIdentities = (source.participantIdentities ?? new List<GameDetailModel.ParticipantIdentitiesItem>())
                .Select(identity => new MatchParticipantIdentity
                {
                    participantId = identity.participantId,
                    player = identity.player == null ? null : new MatchPlayer
                    {
                        Puuid = identity.player.puuid ?? "",
                        GameName = identity.player.gameName ?? "",
                        SummonerName = identity.player.summonerName ?? "",
                        TagLine = identity.player.tagLine ?? ""
                    }
                })
                .ToList(),
            participants = (source.participants ?? new List<GameDetailModel.ParticipantsItem>())
                .Select(participant => new MatchParticipant
                {
                    championId = participant.championId,
                    participantId = participant.participantId,
                    Spell1Id = ParseSpellId(participant.Spell1Id),
                    Spell2Id = ParseSpellId(participant.Spell2Id),
                    teamId = participant.teamId,
                    stats = ToDomain(participant.stats)
                })
                .ToList(),
            teams = (source.teams ?? new List<GameDetailModel.TeamsItem>())
                .Select(team => new MatchTeam
                {
                    TeamId = team.teamId,
                    BannedChampionIds = (team.bans ?? new List<GameDetailModel.BansItem>())
                        .Where(ban => ban.championId > 0)
                        .OrderBy(ban => ban.pickTurn)
                        .Select(ban => ban.championId)
                        .ToList()
                }).ToList()
        };
    }

    /// <summary>将旧接口或持久化数据映射为业务层使用的领域对象。</summary>
    private static MatchParticipantStats? ToDomain(GameDetailModel.Stats? source)
    {
        if (source == null) return null;
        return new MatchParticipantStats
        {
            assists = source.assists ?? 0,
            champLevel = source.champLevel,
            deaths = source.deaths ?? 0,
            doubleKills = source.doubleKills,
            goldEarned = source.goldEarned,
            item0 = source.item0,
            item1 = source.item1,
            item2 = source.item2,
            item3 = source.item3,
            item4 = source.item4,
            item5 = source.item5,
            item6 = source.item6,
            kills = source.kills ?? 0,
            neutralMinionsKilled = source.neutralMinionsKilled,
            pentaKills = source.pentaKills,
            quadraKills = source.quadraKills,
            totalDamageDealtToChampions = source.totalDamageDealtToChampions,
            totalDamageTaken = source.totalDamageTaken,
            totalHeal = source.totalHeal,
            totalMinionsKilled = source.totalMinionsKilled,
            totalTimeCrowdControlDealt = source.totalTimeCrowdControlDealt,
            tripleKills = source.tripleKills,
            visionScore = source.visionScore,
            Win = IsWin(source.win),
            HasWinResult = HasWinResult(source.win),
            HasKdaResult = source.kills.HasValue && source.deaths.HasValue && source.assists.HasValue,
            RuneIds = new[] { source.perk0, source.perk1, source.perk2, source.perk3, source.perk4, source.perk5 }
                .Where(id => id > 0).ToList(),
            AugmentIds = new[]
                {
                    source.playerAugment1, source.playerAugment2, source.playerAugment3,
                    source.playerAugment4, source.playerAugment5, source.playerAugment6
                }
                .Where(id => id > 0)
                .ToList()
        };
    }

    /// <summary>战绩列表摘要的参赛数据；字段与详情接口一致，但胜负已是布尔值。</summary>
    private static MatchParticipantStats? ToDomain(GameHeadModel.ParticipantStats? source)
    {
        if (source == null) return null;
        return new MatchParticipantStats
        {
            assists = source.Assists ?? 0,
            champLevel = source.ChampLevel,
            deaths = source.Deaths ?? 0,
            doubleKills = source.DoubleKills,
            goldEarned = source.GoldEarned,
            item0 = source.Item0,
            item1 = source.Item1,
            item2 = source.Item2,
            item3 = source.Item3,
            item4 = source.Item4,
            item5 = source.Item5,
            item6 = source.Item6,
            kills = source.Kills ?? 0,
            neutralMinionsKilled = source.NeutralMinionsKilled,
            pentaKills = source.PentaKills,
            quadraKills = source.QuadraKills,
            totalDamageDealtToChampions = source.TotalDamageDealtToChampions,
            totalDamageTaken = source.TotalDamageTaken,
            totalHeal = source.TotalHeal,
            totalMinionsKilled = source.TotalMinionsKilled,
            totalTimeCrowdControlDealt = source.TotalTimeCrowdControlDealt,
            tripleKills = source.TripleKills,
            visionScore = source.VisionScore,
            Win = source.Win == true,
            HasWinResult = source.Win.HasValue,
            HasKdaResult = source.Kills.HasValue && source.Deaths.HasValue && source.Assists.HasValue
        };
    }

    /// <summary>兼容 LCU 在不同版本中返回的布尔、数字和文本胜负字段。</summary>
    private static bool IsWin(object? value)
    {
        if (value is bool result) return result;
        return Convert.ToString(value)?.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "win" or "won" or "victory" or "success" or "胜" or "胜利" or "赢" => true,
            _ => false
        };
    }

    private static bool HasWinResult(object? value) => value is bool ||
        Convert.ToString(value)?.Trim().ToLowerInvariant() is
        "true" or "1" or "win" or "won" or "victory" or "success" or "胜" or "胜利" or "赢" or
        "false" or "0" or "loss" or "lose" or "lost" or "defeat" or "fail" or "失败" or "输";

    /// <summary>解析召唤师技能标识，无效值按未提供处理。</summary>
    private static int ParseSpellId(string? value) => int.TryParse(value, out int id) ? id : 0;
}
