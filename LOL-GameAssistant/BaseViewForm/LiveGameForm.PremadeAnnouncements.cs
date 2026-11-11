using LOL_GameAssistant.Domain.Teams;
using LOL_GameAssistant.Domain.ChampionSelect;
using LOL_GameAssistant.Bootstrap;
using LOL_GameAssistant.Helper;

namespace LOL_GameAssistant.BaseViewForm;

public partial class LiveGameForm
{
    private Task _historyPartyTask = Task.CompletedTask;

    private async Task WaitForPremadeAnnouncementAsync(CancellationToken token = default)
    {
        if (!AppCompositionRoot.ApplicationSettingsStore.Load().GameKdaPremadeAnnouncementEnabled) return;
        // 卡片可能同步发布缓存评分；先让本轮阵容渲染启动对应的开黑检测。
        await Task.Yield();
        try { await _historyPartyTask.WaitAsync(TimeSpan.FromSeconds(10), token.CanBeCanceled ? token : _dataLifetime.Token); }
        catch (TimeoutException)
        {
            RuntimeDiagnostics.Report("KDA 开黑提示", "检测未完成", "本次先发送已确认的组队信息和 KDA，不等待更多历史查询。");
        }
        catch (OperationCanceledException) when (_dataLifetime.IsCancellationRequested) { }
    }

    /// <summary>先列我方、再列敌方当前名单内的开黑组，发送文案统一显示“开黑”。</summary>
    internal IReadOnlyList<string> BuildPremadeAnnouncementMessages(bool enabled)
    {
        if (!enabled) return [];
        string? self = _encounterSelfPuuid ?? _myPuuid;
        if (string.IsNullOrWhiteSpace(self)) return [];
        string? ownTeam = _gameAssessmentRoster.FirstOrDefault(member => member.Puuid == self).Team;
        if (ownTeam is not ("蓝方" or "红方") || _currentPartyDetection == null) return [];
        int ownIndex = ownTeam == "蓝方" ? 0 : 1;
        return new[] { ownIndex, 1 - ownIndex }.SelectMany(teamIndex =>
        {
            var members = _gameAssessmentRoster.Where(member => member.Team == (teamIndex == 0 ? "蓝方" : "红方"))
                .Select(member => (member.Puuid, member.Name)).ToArray();
            return BuildPremadeMessagesForTeam(teamIndex, teamIndex == ownIndex ? "我方" : "敌方", members,
                puuid => _gameAssessments.GetValueOrDefault(puuid)?.DisplayName);
        }).ToArray();
    }

    internal string AddChampSelectPremadeMessages(string kda, ChampionSelectionSnapshot session, bool enabled)
    {
        if (!enabled || _currentPartyDetection == null) return kda;
        var cards = panelTeam1.Controls.OfType<LivePlayerForm>().Concat(panelTeam2.Controls.OfType<LivePlayerForm>()).ToArray();
        var allies = session.MyTeam.OrderBy(member => member.CellId)
            .Select(member => (member.Puuid, Name: "")).ToArray();
        var enemies = session.TheirTeam.OrderBy(member => member.CellId)
            .Select(member => (member.Puuid, Name: "")).ToArray();
        // 选人阵容按 MyTeam / TheirTeam 渲染，分别对应检测结果中的第一、第二队。
        string? ResolveName(string puuid) => cards.FirstOrDefault(card => card.Puuid == puuid)?.AnnouncementDisplayName;
        var prefixes = BuildPremadeMessagesForTeam(0, "我方", allies, ResolveName)
            .Concat(BuildPremadeMessagesForTeam(1, "敌方", enemies, ResolveName));
        return string.Join("\n", prefixes.Append(kda));
    }

    private IReadOnlyList<string> BuildPremadeMessagesForTeam(int teamIndex, string side,
        (string Puuid, string Name)[] members, Func<string, string?> resolveName)
    {
        if (_currentPartyDetection == null) return [];
        var detection = _currentPartyDetection.WithHistoryFallback(_historyParties);
        var groups = detection.Groups.Groups.Where(group => group.TeamIndex == teamIndex && group.Puuids.Count >= 2 &&
                group.Puuids.All(puuid => members.Any(member => member.Puuid == puuid)))
            .OrderBy(group => group.Puuids.Min(puuid => Array.FindIndex(members, member => member.Puuid == puuid)))
            .ToArray();
        return groups.Select((group, groupIndex) =>
            {
                var names = group.Puuids.Select((puuid, index) =>
                {
                    var member = members.First(member => member.Puuid == puuid);
                    string? name = resolveName(puuid);
                    if (IsPlaceholder(name)) name = member.Name;
                    if (IsPlaceholder(name)) name = group.Names.ElementAtOrDefault(index);
                    if (IsPlaceholder(name)) name = $"{side}{Array.FindIndex(members, member => member.Puuid == puuid) + 1}楼";
                    return string.Concat(string.Concat(name!.Where(character => !char.IsControl(character)))
                        .Trim().EnumerateRunes().Take(16).Select(rune => rune.ToString()));
                });
                string squad = groups.Length > 1
                    ? groupIndex switch { 0 => "一队", 1 => "二队", _ => $"{groupIndex + 1}队" }
                    : "";
                return $"【{side}开黑{squad}·{group.Puuids.Count}人】{string.Join("、", names)}";
            }).ToArray();
    }

    private static bool IsPlaceholder(string? name) => string.IsNullOrWhiteSpace(name) ||
        name is "加载玩家信息…" or "加载玩家信息..." or "未知玩家";
}
