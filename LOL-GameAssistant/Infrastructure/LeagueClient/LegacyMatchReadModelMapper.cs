using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Entity;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>
/// 旧 LCU 战绩 DTO 到领域读模型的集中映射。
/// 历史字段大小写和 Win 多类型兼容均停留在基础设施层。
/// </summary>
internal static class LegacyMatchReadModelMapper
{
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
                            .ToList()
                    })
                    .ToList()
            }
        };
    }

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
                .ToList()
        };
    }

    private static MatchParticipantStats? ToDomain(GameDetailModel.Stats? source)
    {
        if (source == null) return null;
        return new MatchParticipantStats
        {
            assists = source.assists,
            champLevel = source.champLevel,
            deaths = source.deaths,
            doubleKills = source.doubleKills,
            goldEarned = source.goldEarned,
            item0 = source.item0,
            item1 = source.item1,
            item2 = source.item2,
            item3 = source.item3,
            item4 = source.item4,
            item5 = source.item5,
            item6 = source.item6,
            kills = source.kills,
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
            Win = IsWin(source.win)
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

    private static int ParseSpellId(string? value) => int.TryParse(value, out int id) ? id : 0;
}
