namespace LOL_GameAssistant.Application.GameData;

/// <summary>按强化标识解析名称、图标和稀有度的展示接口。</summary>
public interface IAugmentInfoService
{
    /// <summary>解析给定标识对应的展示信息。</summary>
    Task<IReadOnlyList<AugmentInfo>> ResolveAsync(IEnumerable<int> ids);
}

/// <summary>强化展示信息，包括名称、图标地址和稀有度。</summary>
public sealed record AugmentInfo(int Id, string Name, string? IconUrl, string? EnglishName = null, int? Rarity = null);
