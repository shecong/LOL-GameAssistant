using LOL_GameAssistant.Domain.GameData;

namespace LOL_GameAssistant.Application.GameData;

/// <summary>查询英雄展示名称的只读目录端口。</summary>
public interface IChampionCatalog
{
    string GetDisplayName(int championId);

    IReadOnlyList<ChampionReference> GetAll();

    int? FindIdByDisplayName(string? displayName);
}
