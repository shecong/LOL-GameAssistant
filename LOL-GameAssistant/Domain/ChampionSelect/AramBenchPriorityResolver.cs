namespace LOL_GameAssistant.Domain.ChampionSelect;

/// <summary>根据配置优先级选择大乱斗备选英雄。</summary>
public static class AramBenchPriorityResolver
{
    /// <summary>按当前优先规则选择合适的大乱斗备选英雄。</summary>
    public static int? Choose(int currentChampionId, IEnumerable<int> benchIds, IReadOnlyList<int> priorityIds)
    {
        // 当前英雄不在偏好列表时，任意有效偏好英雄都可替换；列表中已有英雄则只向更高优先级升级。
        int currentRank = int.MaxValue;
        for (int index = 0; index < priorityIds.Count; index++)
            if (priorityIds[index] == currentChampionId) { currentRank = index; break; }
        var bench = benchIds.ToHashSet();
        for (int index = 0; index < priorityIds.Count && index < currentRank; index++)
            if (priorityIds[index] > 0 && bench.Contains(priorityIds[index])) return priorityIds[index];
        return null;
    }
}
