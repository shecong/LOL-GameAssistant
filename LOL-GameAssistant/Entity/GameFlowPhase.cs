using System;
using System.Collections.Generic;
using System.Text;

namespace LOL_GameAssistant.Entity
{
    /// <summary>
    /// 表示游戏客户端的流程阶段。
    /// 对应接口：/lol-gameflow/v1/gameflow-phase
    /// </summary>
    public enum GameFlowPhase
    {
        /// <summary>
        /// 客户端未启动
        /// </summary>
        Closed,

        /// <summary> 客户端启动，无状态 </summary>
        None,

        /// <summary> 位于大厅 </summary>
        Lobby,

        /// <summary> 正在匹配中 </summary>
        Matchmaking,

        /// <summary> 找到对局，等待确认 </summary>
        ReadyCheck,

        /// <summary> 英雄选择阶段 </summary>
        ChampSelect,

        /// <summary> 游戏对局进行中 </summary>
        InProgress,

        /// <summary> 游戏结束，等待结算数据 </summary>
        WaitingForStats,

        /// <summary> 显示结算界面 </summary>
        EndOfGame,

        /// <summary> 需要重新连接至游戏 </summary>
        Reconnect
    }

    /// <summary>
    /// GameFlowPhase 枚举的扩展方法
    /// </summary>
    public static class GameFlowPhaseExtensions
    {
        /// <summary>
        /// 获取枚举值对应的中文名称
        /// </summary>
        /// <param name="phase">游戏流程阶段</param>
        /// <returns>中文名称</returns>
        public static string GetChineseName(this GameFlowPhase phase)
        {
            return phase switch
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
    }
}