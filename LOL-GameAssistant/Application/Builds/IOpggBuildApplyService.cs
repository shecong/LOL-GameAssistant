namespace LOL_GameAssistant.Application.Builds;

/// <summary>从 OP.GG 公共英雄数据获取推荐，并写入本机 League Client 的符文页和物品集。</summary>
public interface IOpggBuildApplyService
{
    Task<OpggBuildApplyResult> ApplyForChampionAsync(
        int championId,
        string? position,
        CancellationToken cancellationToken = default);
}

/// <summary>一次一键配置的可展示结果；不会包含 LCU 凭据或第三方请求内容。</summary>
public sealed record OpggBuildApplyResult(bool Succeeded, string Message)
{
    public static OpggBuildApplyResult Success(string message) => new(true, message);
    public static OpggBuildApplyResult Failure(string message) => new(false, message);
}
