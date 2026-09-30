namespace LOL_GameAssistant.Application.GameData;

public interface IAugmentInfoService
{
    Task<IReadOnlyList<AugmentInfo>> ResolveAsync(IEnumerable<int> ids);
}

public sealed record AugmentInfo(int Id, string Name, string? IconUrl, string? EnglishName = null);
