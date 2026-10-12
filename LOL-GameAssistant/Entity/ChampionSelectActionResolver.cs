namespace LOL_GameAssistant.Entity;

/// <summary>从选人会话中定位本地玩家当前可执行的动作。</summary>
public static class ChampionSelectActionResolver
{
    /// <summary>收集队友预选英雄，供自动禁用避让；不包含本地玩家和对手。</summary>
    public static HashSet<int> CollectTeammatePickIntentIds(ChampSelectSession session)
    {
        return session.MyTeam
            .Where(member => member.CellId != session.LocalPlayerCellId && member.ChampionPickIntent > 0)
            .Select(member => member.ChampionPickIntent)
            .ToHashSet();
    }

    /// <summary>从选人动作列表中确定当前玩家可执行的动作。</summary>
    public static ChampSelectAction? FindCurrentLocalAction(ChampSelectSession session, string actionType)
    {
        foreach (var round in session.Actions)
        {
            foreach (var action in round)
            {
                if (action.ActorCellId == session.LocalPlayerCellId &&
                    action.IsInProgress && !action.Completed &&
                    string.Equals(action.Type, actionType, StringComparison.OrdinalIgnoreCase))
                    return action;
            }
        }
        return null;
    }
}
