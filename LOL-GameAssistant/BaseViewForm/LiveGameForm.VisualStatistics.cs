using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Domain.LeagueClient;
using LOL_GameAssistant.Helper;
using LOL_GameAssistant.Infrastructure.Pengu;

namespace LOL_GameAssistant.BaseViewForm;

public partial class LiveGameForm
{
    private string _visualStatisticsContext = "";
    private string[] _visualStatisticsAllies = [];
    private HashSet<string> _visualStatisticsPlayers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PenguPlayerStatistic> _visualStatistics = new(StringComparer.Ordinal);
    private int _visualStatisticsQueue;

    private bool PrepareVisualStatistics(
        IEnumerable<(string Puuid, string Name, int ChampionId, string Position, bool IsBot)> team1,
        IEnumerable<(string Puuid, string Name, int ChampionId, string Position, bool IsBot)> team2,
        string? self, int queueId)
    {
        if (Program.GameMain.gameFlowPhase != GameFlowPhase.ChampSelect) return false;
        var first = team1.ToArray();
        var second = team2.ToArray();
        var allies = first.Any(member => member.Puuid == self) ? first : second.Any(member => member.Puuid == self) ? second : [];
        _visualStatisticsAllies = allies.Where(member => !member.IsBot && !string.IsNullOrWhiteSpace(member.Puuid))
            .Select(member => member.Puuid).Order(StringComparer.Ordinal).ToArray();
        _visualStatisticsPlayers = first.Concat(second).Where(member => !member.IsBot && !string.IsNullOrWhiteSpace(member.Puuid))
            .Select(member => member.Puuid).ToHashSet(StringComparer.Ordinal);
        string context = PlayerHistorySession.Key + ":" + queueId + ":" + string.Join(",", _visualStatisticsAllies) +
            ":" + string.Join(",", _visualStatisticsPlayers.Order(StringComparer.Ordinal));
        if (context == _visualStatisticsContext) return false;
        _visualStatisticsContext = context;
        _visualStatisticsQueue = queueId;
        _visualStatistics.Clear();
        PublishVisualStatistics();
        return true;
    }

    private void PublishVisualStatistics()
    {
        try
        {
            new PenguVisualSettingsService(() => AppCompositionRoot.PenguPlugin.PluginsDirectory).PublishStatistics(
                _visualStatisticsContext.Length == 0 ? null : new(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    _visualStatisticsQueue, _visualStatisticsAllies, new Dictionary<string, PenguPlayerStatistic>(_visualStatistics)));
        }
        catch (Exception error) { RuntimeDiagnostics.WriteException(error); }
    }
}
