namespace LOL_GameAssistant.Application.Players;

public sealed record PlayerSpectateResult(bool Accepted, string Message);

/// <summary>尝试通过当前客户端观战指定玩家，不以好友关系或已知对局 ID 为前提。</summary>
public interface IPlayerSpectateService
{
    Task<PlayerSpectateResult> LaunchAsync(string puuid, CancellationToken cancellationToken = default);
}
