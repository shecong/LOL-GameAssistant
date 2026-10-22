using LOL_GameAssistant.Application.Matches;

namespace LOL_GameAssistant.Application.GameData;

public sealed record PatchWatchResult(string PreviousVersion, string CurrentVersion, IReadOnlyList<string> Changes,
    IReadOnlyDictionary<string, TrackedPatchAsset> Assets, string Message);
public interface IPatchWatchService
{
    Task<PatchWatchResult> CheckAsync(PlayerExperience previous, IEnumerable<int> champions, IEnumerable<int> items, CancellationToken token = default, IEnumerable<int>? runes = null);
}
