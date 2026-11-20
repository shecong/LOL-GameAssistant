using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LOL_GameAssistant.Application.Matches;

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
            value.FavoriteTeammates ??= new();
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
            Directory.CreateDirectory(_directory);
            string temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); File.Move(temporary, path, true);
        }
        finally { gate.Release(); }
    }
}
