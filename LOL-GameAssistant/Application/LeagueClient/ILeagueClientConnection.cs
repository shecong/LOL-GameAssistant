namespace LOL_GameAssistant.Application.LeagueClient;

/// <summary>确保本机 LCU 已连接的应用端口。</summary>
public interface ILeagueClientConnection
{
    /// <summary>检测并应用当前 LCU 认证信息；客户端未启动时返回 false。</summary>
    bool TryConnect();

}
