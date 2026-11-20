namespace LOL_GameAssistant.Application.Matches;

public sealed class PlayerExperience
{
    public Dictionary<string, string> FavoriteTeammates { get; set; } = new();
    public string PlayStyle { get; set; } = "轻松娱乐";
    public string PreferredPosition { get; set; } = "任意位置";
    public bool Voice { get; set; }
    public string AvailableTime { get; set; } = "";
}
public interface ICompanionExperienceStore
{
    Task<PlayerExperience> LoadAsync(string player, CancellationToken token = default);
    Task UpdateAsync(string player, Action<PlayerExperience> update, CancellationToken token = default);
}
