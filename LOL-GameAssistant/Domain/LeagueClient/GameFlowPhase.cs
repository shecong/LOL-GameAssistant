namespace LOL_GameAssistant.Domain.LeagueClient;

/// <summary>LOL 客户端对局流程阶段。</summary>
public enum GameFlowPhase
{
    Closed,
    None,
    Lobby,
    Matchmaking,
    ReadyCheck,
    ChampSelect,
    InProgress,
    WaitingForStats,
    EndOfGame,
    Reconnect
}

/// <summary>流程阶段的中文展示规则。</summary>
public static class GameFlowPhaseExtensions
{
    public static string GetChineseName(this GameFlowPhase phase) => phase switch
    {
        GameFlowPhase.Closed => "客户端未启动",
        GameFlowPhase.None => "客户端启动，无状态",
        GameFlowPhase.Lobby => "位于大厅",
        GameFlowPhase.Matchmaking => "正在匹配中",
        GameFlowPhase.ReadyCheck => "找到对局，等待确认",
        GameFlowPhase.ChampSelect => "英雄选择阶段",
        GameFlowPhase.InProgress => "游戏对局进行中",
        GameFlowPhase.WaitingForStats => "游戏结束，等待结算数据",
        GameFlowPhase.EndOfGame => "显示结算界面",
        GameFlowPhase.Reconnect => "需要重新连接至游戏",
        _ => phase.ToString()
    };
}