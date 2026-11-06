using System.Security.Cryptography;
using System.Text;
using LOL_GameAssistant.Application.Skins;
using LOL_GameAssistant.Domain.GameData;

namespace LOL_GameAssistant.Infrastructure.Skins;

/// <summary>Public game artwork with a local disk cache, independent of an active LCU connection.</summary>
public sealed class FileSkinArtworkService(string cacheDirectory) : IFileSkinArtworkService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly SemaphoreSlim _downloads = new(4, 4);
    public Task<GameAsset?> GetChampionAsync(CslolSkinEntry entry, CancellationToken token) => ReadAsync(
        $"https://game.gtimg.cn/images/lol/act/img/champion/{Uri.EscapeDataString(entry.ChampionKey)}.png", token);
    public async Task<GameAsset?> GetSkinAsync(CslolSkinEntry entry, CancellationToken token)
    {
        string model = entry.ChampionKey switch { "Kaisa" => "KaiSa", "Khazix" => "Khazix", "Velkoz" => "Velkoz", _ => entry.ChampionKey };
        return await ReadAsync($"https://game.gtimg.cn/images/lol/act/img/skin/big{entry.SkinId}.jpg", token).ConfigureAwait(false)
            ?? await ReadAsync($"https://ddragon.leagueoflegends.com/cdn/img/champion/loading/{Uri.EscapeDataString(model)}_{entry.SkinNum}.jpg", token).ConfigureAwait(false);
    }
    private async Task<GameAsset?> ReadAsync(string url, CancellationToken token)
    {
        string path = Path.Combine(cacheDirectory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ".img");
        await _downloads.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length is >= 12 and <= 8 * 1024 * 1024)
            {
                var cached = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
                if (ImageBytes(cached)) return new(cached);
            }
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 8 * 1024 * 1024) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
            if (!ImageBytes(bytes)) return null;
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(cacheDirectory); await File.WriteAllBytesAsync(temp, bytes, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested(); File.Move(temp, path, true);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            return new(bytes);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        catch (HttpRequestException) { return null; }
        finally { _downloads.Release(); }
    }
    private static bool ImageBytes(byte[] bytes) => bytes.Length is >= 12 and <= 8 * 1024 * 1024
        && (bytes[0] == 0xff && bytes[1] == 0xd8 || bytes[0] == 0x89 && bytes[1] == 0x50);
}
