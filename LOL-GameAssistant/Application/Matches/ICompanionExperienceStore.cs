using LOL_GameAssistant.Domain.MatchAnalysis;

namespace LOL_GameAssistant.Application.Matches;

public sealed class PlayerExperience
{
    public PracticeGoal? Goal { get; set; }
    public string PatchVersion { get; set; } = "";
    public Dictionary<string, TrackedPatchAsset> PatchAssets { get; set; } = new();
    public Dictionary<string, string> FavoriteTeammates { get; set; } = new();
    public string PlayStyle { get; set; } = "轻松娱乐";
    public string PreferredPosition { get; set; } = "任意位置";
    public bool Voice { get; set; }
    public string AvailableTime { get; set; } = "";
}
public sealed record TrackedPatchAsset(string Name, Dictionary<string, string> Values);
public interface ICompanionExperienceStore
{
    Task<PlayerExperience> LoadAsync(string player, CancellationToken token = default);
    Task UpdateAsync(string player, Action<PlayerExperience> update, CancellationToken token = default);
}
