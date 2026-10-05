using LOL_GameAssistant.Application.Builds;
using LOL_GameAssistant.Domain.Builds;
using LOL_GameAssistant.Infrastructure.GameData;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>推荐方案缓存六小时，游戏更新自动换缓存；所有客户端写入仍交由原服务执行。</summary>
internal sealed class CachedOpggBuildApplyService : IOpggBuildApplyService
{
    private readonly IOpggBuildApplyService _inner;
    private readonly Func<string> _version;
    private readonly LocalDataCache<OpggBuildChoices> _cache;

    /// <summary>初始化 CachedOpggBuildApplyService 的实例状态，并保存传入的依赖或数据。</summary>
    public CachedOpggBuildApplyService(IOpggBuildApplyService inner, Func<string> version, string? directory = null)
    {
        _inner = inner;
        _version = version;
        _cache = new(directory ?? Path.Combine(LocalDataCache<OpggBuildChoices>.Root, "builds"),
            TimeSpan.FromHours(6), choices => choices.Succeeded && choices.ChampionId > 0 &&
                choices.Options is { Count: > 0 } && choices.Options.All(option => option != null &&
                    option.CoreItemIds is { Count: >= 3 } && option.StarterItemIds != null &&
                    option.SituationalItemIds != null && option.RunePerkIds != null));
    }

    /// <summary>读取指定英雄和场景的推荐方案集合。</summary>
    public Task<OpggBuildChoices> GetBuildChoicesAsync(int id, string? position, CancellationToken token = default) =>
        GetBuildChoicesAsync(id, position, new OpggBuildRequest("CLASSIC"), token);

    /// <summary>读取指定英雄和场景的推荐方案集合。</summary>
    public async Task<OpggBuildChoices> GetBuildChoicesAsync(int id, string? position, OpggBuildRequest request,
        CancellationToken token = default)
    {
        string mode = OpggBuildApplyService.NormalizeMode(request.GameMode, request.QueueId);
        if (id <= 0 || mode == "unknown") return await _inner.GetBuildChoicesAsync(id, position, request, token);
        string role = mode == "ranked" ? PersonalRunePresetResolver.NormalizePosition(position) : "none";
        return await _cache.GetAsync($"{_version()}:{id}:{mode}:{role}",
            async () => await _inner.GetBuildChoicesAsync(id, position, request), token)
            ?? OpggBuildChoices.Failure("暂未获取到推荐方案，请重试。");
    }

    /// <summary>读取当前符文页和召唤师技能，生成可保存的个人方案。</summary>
    public Task<PersonalRunePreset?> CaptureCurrentRunePresetAsync(int id, string mode, string position, CancellationToken token = default) =>
        _inner.CaptureCurrentRunePresetAsync(id, mode, position, token);
    /// <summary>将保存的个人符文及技能方案写入客户端。</summary>
    public Task<OpggBuildApplyResult> ApplyPersonalRunePresetAsync(PersonalRunePreset preset, CancellationToken token = default) =>
        _inner.ApplyPersonalRunePresetAsync(preset, token);
    /// <summary>应用用户选择的符文、召唤师技能和装备方案。</summary>
    public Task<OpggBuildApplyResult> ApplyBuildAsync(int id, string? position, OpggBuildOption option,
        CancellationToken token = default, bool allowReplaceCurrentRunePage = false) =>
        _inner.ApplyBuildAsync(id, position, option, token, allowReplaceCurrentRunePage);
    /// <summary>读取指定英雄的推荐方案并执行应用流程。</summary>
    public async Task<OpggBuildApplyResult> ApplyForChampionAsync(int id, string? position, CancellationToken token = default)
    {
        var choices = await GetBuildChoicesAsync(id, position, token);
        return choices.Succeeded && choices.Options.FirstOrDefault() is { } option
            ? await ApplyBuildAsync(id, position, option, token) : OpggBuildApplyResult.Failure(choices.Message);
    }
}
