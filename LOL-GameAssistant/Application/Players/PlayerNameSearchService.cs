using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Domain.Matches;
using LOL_GameAssistant.Domain.Players;

namespace LOL_GameAssistant.Application.Players;

public sealed record PlayerSearchCandidate(string Puuid, string GameName, string TagLine, string Platform,
    int? Level, string Source)
{
    public string RiotId => string.IsNullOrWhiteSpace(TagLine) ? GameName : $"{GameName}#{TagLine}";
}

public sealed record PlayerNameSearchResult(IReadOnlyList<PlayerSearchCandidate> Candidates, bool IsComplete, string ScopeMessage);

/// <summary>合并所有可读取的候选，不将单结果名称接口误认为全区同名枚举接口。</summary>
public sealed class PlayerNameSearchService(IPlayerProfileService profiles, IMatchHistoryService history)
{
    public async Task<PlayerNameSearchResult> SearchAsync(string input, string platform, IReadOnlyList<FavoritePlayer> favorites,
        IReadOnlyList<MatchHistoryGame> knownGames, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        input = input.Trim();
        var parts = input.Split('#', 2);
        string name = parts[0].Trim();
        string? tag = parts.Length == 2 ? parts[1].Trim().TrimStart('#') : null;
        if (name.Length == 0 || tag is { Length: 0 }) throw new ArgumentException("请输入召唤师名，或完整的名称#TAG。");
        var found = new Dictionary<string, PlayerSearchCandidate>(StringComparer.Ordinal);
        var notes = new List<string>();
        bool lookupSupported = true;
        bool Matches(string gameName, string tagLine) => string.Equals(gameName.Trim(), name, StringComparison.OrdinalIgnoreCase) &&
            (tag == null || string.Equals(tagLine.Trim().TrimStart('#'), tag, StringComparison.OrdinalIgnoreCase));
        void Add(PlayerSearchCandidate candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate.Puuid) || !Matches(candidate.GameName, candidate.TagLine)) return;
            if (found.TryGetValue(candidate.Puuid, out var previous))
                found[candidate.Puuid] = previous with
                {
                    Level = previous.Level ?? candidate.Level,
                    Source = string.Join("、", previous.Source.Split('、').Append(candidate.Source).Distinct())
                };
            else found.Add(candidate.Puuid, candidate);
        }
        void AddGames(IEnumerable<MatchHistoryGame> games, string source)
        {
            foreach (var game in games)
            {
                if (platform.Length > 0 && game.PlatformId.Length > 0 && !string.Equals(game.PlatformId, platform, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var identity in game.ParticipantIdentities)
                {
                    var player = identity.Player;
                    if (player == null) continue;
                    Add(new(player.Puuid, string.IsNullOrWhiteSpace(player.GameName) ? player.SummonerName : player.GameName,
                        player.TagLine, platform, null, source));
                }
            }
        }

        // The live exact Riot ID lookup is unique. Plain-name lookup is NOT proof of uniqueness.
        try
        {
            var player = tag == null ? await profiles.FindByNameAsync(name, token).ConfigureAwait(false)
                : await profiles.FindByRiotIdAsync(name, tag, token).ConfigureAwait(false);
            if (player != null && Matches(player.GameName, player.TagLine))
            {
                Add(new(player.Puuid, player.GameName, player.TagLine, platform, player.SummonerLevel > 0 ? player.SummonerLevel : null, player.IsCached ? "名称缓存" : "名称接口"));
                if (tag != null && !player.IsCached) return new(found.Values.ToArray(), true, "完整 Riot ID 精确匹配。");
            }
        }
        catch (NotSupportedException) { lookupSupported = false; notes.Add("所选大区暂不支持跨区名称接口"); }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidOperationException)
        { notes.Add("名称接口暂不可用"); }
        token.ThrowIfCancellationRequested();

        foreach (var favorite in favorites.Where(f => string.Equals(f.Platform ?? "", platform, StringComparison.OrdinalIgnoreCase)))
            Add(new(favorite.Puuid, favorite.GameName, favorite.TagLine, platform,
                int.TryParse(favorite.SummonerLevel, out int level) && level > 0 ? level : null, "收藏"));
        AddGames(knownGames, "已加载战绩");
        if (lookupSupported)
        {
            try
            {
                var self = await profiles.GetCurrentAsync(token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(self?.Puuid))
                {
                    if (Matches(self.GameName, self.TagLine)) Add(new(self.Puuid, self.GameName, self.TagLine, platform,
                        self.SummonerLevel > 0 ? self.SummonerLevel : null, "当前玩家"));
                    var recent = await history.GetPageAsync(self.Puuid, 0, 199, token).ConfigureAwait(false);
                    AddGames(recent?.Games?.Games ?? [], "近期200场");
                }
            }
            catch (Exception error) when (error is HttpRequestException or IOException or NotSupportedException)
            { notes.Add("近期战绩未能完整读取"); }
        }
        token.ThrowIfCancellationRequested();
        var candidates = found.Values.OrderBy(c => c.GameName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.TagLine, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Puuid, StringComparer.Ordinal).ToArray();
        string scope = "候选来自名称接口、所选大区收藏及可读的近期/已加载战绩，不是全区同名名单。完整名称#TAG 或 PUUID 可缩小范围。";
        if (notes.Count > 0) scope += " " + string.Join("；", notes) + "。";
        return new(candidates, false, scope);
    }
}
