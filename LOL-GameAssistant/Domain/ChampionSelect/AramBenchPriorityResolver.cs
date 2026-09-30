namespace LOL_GameAssistant.Domain.ChampionSelect;

public static class AramBenchPriorityResolver
{
    public static int? Choose(int currentChampionId, IEnumerable<int> benchIds, IReadOnlyList<int> priorityIds)
    {
        int currentRank = int.MaxValue;
        for (int index = 0; index < priorityIds.Count; index++)
            if (priorityIds[index] == currentChampionId) { currentRank = index; break; }
        var bench = benchIds.ToHashSet();
        for (int index = 0; index < priorityIds.Count && index < currentRank; index++)
            if (priorityIds[index] > 0 && bench.Contains(priorityIds[index])) return priorityIds[index];
        return null;
    }
}
