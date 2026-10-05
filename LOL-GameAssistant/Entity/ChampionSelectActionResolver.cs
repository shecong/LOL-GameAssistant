namespace LOL_GameAssistant.Entity;

/// <summary>从选人会话中定位本地玩家当前可执行的动作。</summary>
public static class ChampionSelectActionResolver
{
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