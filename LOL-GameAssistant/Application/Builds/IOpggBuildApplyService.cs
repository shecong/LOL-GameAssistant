namespace LOL_GameAssistant.Application.Builds;

/// <summary>从 OP.GG 公共英雄数据获取推荐，并写入本机 League Client 的符文页和物品集。</summary>
public interface IOpggBuildApplyService
{
    /// <summary>读取当前英雄、分路下可供用户选择的 OP.GG 出装路线，不会写入客户端。</summary>
    Task<OpggBuildChoices> GetBuildChoicesAsync(
        int championId,
        string? position,
        CancellationToken cancellationToken = default);

    /// <summary>将用户明确选择的一条路线写入当前符文页和自定义物品集。</summary>
    Task<OpggBuildApplyResult> ApplyBuildAsync(
        int championId,
        string? position,
        OpggBuildOption option,
        CancellationToken cancellationToken = default);

    /// <summary>兼容旧调用方：直接应用使用率最高的路线。</summary>
    Task<OpggBuildApplyResult> ApplyForChampionAsync(
        int championId,
        string? position,
        CancellationToken cancellationToken = default);
}

/// <summary>OP.GG 返回的单条核心出装路线及其样本信息。</summary>
public sealed record OpggBuildOption(
    int Order,
    IReadOnlyList<int> StarterItemIds,
    IReadOnlyList<int> CoreItemIds,
    IReadOnlyList<int> SituationalItemIds,
    int PrimaryStyleId,
    int SubStyleId,
    IReadOnlyList<int> RunePerkIds,
    int Matches,
    int Wins)
{
    public double WinRate => Matches <= 0 ? 0 : Math.Round(Wins * 100D / Matches, 1);
}

/// <summary>弹窗展示所需的 OP.GG 路线集合；失败时 Options 为空且 Message 可直接展示。</summary>
public sealed record OpggBuildChoices(
    bool Succeeded,
    string Message,
    string ChampionName,
    string PositionName,
    IReadOnlyList<OpggBuildOption> Options)
{
    public static OpggBuildChoices Failure(string message) => new(false, message, "", "", Array.Empty<OpggBuildOption>());
}

/// <summary>一次一键配置的可展示结果；不会包含 LCU 凭据或第三方请求内容。</summary>
public sealed record OpggBuildApplyResult(bool Succeeded, string Message)
{
    public static OpggBuildApplyResult Success(string message) => new(true, message);
    public static OpggBuildApplyResult Failure(string message) => new(false, message);
}
