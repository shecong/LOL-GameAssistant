using LOL_GameAssistant.Domain.GameData;

namespace LOL_GameAssistant.Application.GameData;

/// <summary>查询英雄展示名称的只读目录端口。</summary>
public interface IChampionCatalog
{
    /// <summary>读取指定英雄的展示名称。</summary>
    string GetDisplayName(int championId);

    /// <summary>返回当前目录中的全部条目。</summary>
    IReadOnlyList<ChampionReference> GetAll();

    /// <summary>根据英雄展示名称查找英雄标识。</summary>
    int? FindIdByDisplayName(string? displayName);
}