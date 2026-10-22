using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LOL_GameAssistant.Application.Matches;
using LOL_GameAssistant.Domain.MatchAnalysis;

namespace LOL_GameAssistant.Infrastructure.Files;

public sealed class CompanionExperienceStore(string? directory = null) : ICompanionExperienceStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LOL-GameAssistant", "experience");
    private string PathFor(string player)
    {
        if (string.IsNullOrWhiteSpace(player)) throw new ArgumentException("请先选择玩家");
        return Path.Combine(_directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(player))) + ".json");
    }
    public async Task<PlayerExperience> LoadAsync(string player, CancellationToken token = default)
    {
        string path = PathFor(player);
        try
        {
            var value = JsonSerializer.Deserialize<PlayerExperience>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false)) ?? new();
            value.PatchAssets ??= new(); value.FavoriteTeammates ??= new();
            return value;
        }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        // A malformed file is surfaced instead of silently overwriting personal data.
    }
    public async Task UpdateAsync(string player, Action<PlayerExperience> update, CancellationToken token = default)
    {
        string path = PathFor(player); var gate = Gates.GetOrAdd(path, _ => new(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var value = await LoadAsync(player, token).ConfigureAwait(false); update(value);
            if (value.Goal is { } goal && (!Enum.IsDefined(goal.Metric) || !double.IsFinite(goal.Target) || goal.Target < 0 || (goal.Target == 0 && goal.Metric != PracticeMetric.Deaths) || goal.Games is < 1 or > 30 || string.IsNullOrWhiteSpace(goal.Mode) || goal.Mode == "全部"))
                throw new ArgumentException("请为单一模式设置有效的练习目标");
            Directory.CreateDirectory(_directory);
            string temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); File.Move(temporary, path, true);
        }
        finally { gate.Release(); }
    }
}
