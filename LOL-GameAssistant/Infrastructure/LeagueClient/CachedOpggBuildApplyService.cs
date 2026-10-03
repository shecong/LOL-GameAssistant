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

    public Task<OpggBuildChoices> GetBuildChoicesAsync(int id, string? position, CancellationToken token = default) =>
        GetBuildChoicesAsync(id, position, new OpggBuildRequest("CLASSIC"), token);

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

    public Task<PersonalRunePreset?> CaptureCurrentRunePresetAsync(int id, string mode, string position, CancellationToken token = default) =>
        _inner.CaptureCurrentRunePresetAsync(id, mode, position, token);
    public Task<OpggBuildApplyResult> ApplyPersonalRunePresetAsync(PersonalRunePreset preset, CancellationToken token = default) =>
        _inner.ApplyPersonalRunePresetAsync(preset, token);
    public Task<OpggBuildApplyResult> ApplyBuildAsync(int id, string? position, OpggBuildOption option,
        CancellationToken token = default, bool allowReplaceCurrentRunePage = false) =>
        _inner.ApplyBuildAsync(id, position, option, token, allowReplaceCurrentRunePage);
    public async Task<OpggBuildApplyResult> ApplyForChampionAsync(int id, string? position, CancellationToken token = default)
    {
        var choices = await GetBuildChoicesAsync(id, position, token);
        return choices.Succeeded && choices.Options.FirstOrDefault() is { } option
            ? await ApplyBuildAsync(id, position, option, token) : OpggBuildApplyResult.Failure(choices.Message);
    }
}
