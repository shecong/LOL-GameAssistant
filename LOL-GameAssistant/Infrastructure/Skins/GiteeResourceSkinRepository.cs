using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LOL_GameAssistant.Application.Skins;

namespace LOL_GameAssistant.Infrastructure.Skins;

/// <summary>Wind's Gitee contents/raw protocol with caller-provided credentials.</summary>
public sealed class GiteeResourceSkinRepository : IResourceSkinRepository, IDisposable
{
    private const long MaxBytes = 2L * 1024 * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly string _cache;
    private readonly SemaphoreSlim _downloads = new(1, 1);
    public GiteeResourceSkinRepository(string cache, HttpMessageHandler? handler = null)
    {
        _cache = System.IO.Path.GetFullPath(cache);
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(10) };
    }
    public async Task<IReadOnlyList<ResourceRepositoryEntry>> ListAsync(ResourceRepositoryOptions options, string path, CancellationToken token = default)
    {
        Validate(options); ValidatePath(path, true);
        using var response = await SendAsync(Url(options, "contents", path), options, false, token).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        await CopyLimitedAsync(stream, buffer, 8 * 1024 * 1024, null, token).ConfigureAwait(false); buffer.Position = 0;
        using var json = await JsonDocument.ParseAsync(buffer, cancellationToken: token).ConfigureAwait(false);
        if (json.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("仓库目录返回格式不正确");
        var entries = new List<ResourceRepositoryEntry>();
        foreach (var item in json.RootElement.EnumerateArray())
        {
            string name = Text(item, "name"), remotePath = Text(item, "path");
            bool directory = Text(item, "type") == "dir";
            if (!directory && (Text(item, "type") != "file" || !IsWad(name))) continue;
            ValidatePath(remotePath, false);
            if (remotePath != (path.Length == 0 ? name : path + "/" + name) || name != System.IO.Path.GetFileName(name) || name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException("仓库返回了无效的资源路径");
            long size = item.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out var value) ? value : 0;
            entries.Add(new(name, remotePath, directory, size, Text(item, "sha")));
        }
        return entries.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
    public async Task<string> DownloadAsync(ResourceRepositoryOptions options, ResourceRepositoryEntry entry,
        IProgress<ResourceDownloadProgress>? progress = null, CancellationToken token = default)
    {
        Validate(options); ValidatePath(entry.Path, false);
        if (entry.IsDirectory || !IsWad(entry.Name) || entry.Name != System.IO.Path.GetFileName(entry.Path) || entry.Name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("请选择 WAD 资源文件");
        if (entry.Size > MaxBytes) throw new InvalidDataException("单个资源不能超过 2 GB");
        await _downloads.WaitAsync(token).ConfigureAwait(false);
        string? temporary = null;
        try
        {
            string identity = string.Join('\n', options.Owner, options.Repository, options.Branch, entry.Path, entry.Sha);
            string folder = System.IO.Path.Combine(_cache, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))));
            Directory.CreateDirectory(folder);
            string target = System.IO.Path.Combine(folder, entry.Name);
            temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using var response = await SendAsync(Url(options, "raw", entry.Path), options, true, token).ConfigureAwait(false);
            long? length = response.Content.Headers.ContentLength;
            if (length > MaxBytes) throw new InvalidDataException("下载资源超过 2 GB");
            await using (var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await CopyLimitedAsync(input, output, MaxBytes, bytes => progress?.Report(new(entry.Name, bytes, length)), token).ConfigureAwait(false);
            long received = new FileInfo(temporary).Length;
            if ((length.HasValue && received != length) || (entry.Size > 0 && received != entry.Size)) throw new InvalidDataException("下载未完成：资源长度与目录记录不符");
            try { ResourceSkinService.ValidatePackage(temporary, entry.Name); }
            catch (EndOfStreamException) { throw new InvalidDataException("下载资源已截断或不是有效的 WAD 文件"); }
            if (Regex.IsMatch(entry.Sha, "\\A[0-9a-fA-F]{40}\\z"))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
                hash.AppendData(Encoding.ASCII.GetBytes($"blob {received}\0"));
                await using var data = File.OpenRead(temporary);
                var bytes = new byte[81920]; int read;
                while ((read = await data.ReadAsync(bytes, token).ConfigureAwait(false)) > 0) hash.AppendData(bytes, 0, read);
                if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(entry.Sha, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("下载资源 Git 哈希校验失败");
            }
            token.ThrowIfCancellationRequested(); File.Move(temporary, target, true);
            return target;
        }
        catch (HttpRequestException) { throw new IOException("资源服务器连接失败，请检查网络后重试"); }
        finally
        {
            try { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); }
            finally { _downloads.Release(); }
        }
    }
    private async Task<HttpResponseMessage> SendAsync(Uri uri, ResourceRepositoryOptions options, bool redirects, CancellationToken token)
    {
        for (int count = 0; count < 5; count++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("sc-assistant-resource-skins/1.0");
            // Never put credentials into URLs, cache keys, logs or redirected requests.
            if (count == 0 && options.AccessToken.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("token", options.AccessToken);
            HttpResponseMessage response;
            try { response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false); }
            catch (HttpRequestException) { throw new IOException("资源服务器连接失败，请检查网络后重试"); }
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location; response.Dispose();
                if (!redirects || location == null) throw new IOException("资源接口发生重定向，请检查仓库地址");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                if (uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort || uri.Query.Contains("access_token", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("资源接口返回了不支持的下载地址");
                continue;
            }
            if (response.IsSuccessStatusCode) return response;
            int status = (int)response.StatusCode; response.Dispose();
            throw new IOException(status switch {
                401 => "Gitee 授权失败，请填写你获授权的访问令牌",
                403 => "Gitee 拒绝访问或触发限流；原软件的大资源下载需要获授权的令牌",
                404 => "仓库或资源不存在，或当前身份没有访问权限；请检查仓库地址及你获授权的令牌",
                429 => "Gitee 请求过于频繁，请稍后重试",
                _ => $"资源接口请求失败（HTTP {status}）"
            });
        }
        throw new IOException("资源下载重定向次数过多");
    }
    private static Uri Url(ResourceRepositoryOptions options, string operation, string path)
    {
        string uri = $"https://gitee.com/api/v5/repos/{Uri.EscapeDataString(options.Owner)}/{Uri.EscapeDataString(options.Repository)}/{operation}/" + string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        if (options.Branch.Length > 0) uri += "?ref=" + Uri.EscapeDataString(options.Branch);
        return new Uri(uri);
    }
    private static void Validate(ResourceRepositoryOptions options)
    {
        if (!Regex.IsMatch(options.Owner, "\\A[A-Za-z0-9_][A-Za-z0-9_-]*\\z") || !Regex.IsMatch(options.Repository, "\\A[A-Za-z0-9_][A-Za-z0-9_.-]*\\z")) throw new ArgumentException("仓库格式应为 owner/repository");
        if (options.AccessToken.Any(char.IsWhiteSpace) || options.AccessToken.Any(char.IsControl) || options.AccessToken.Length > 512) throw new ArgumentException("访问令牌格式不正确");
    }
    private static void ValidatePath(string path, bool rootAllowed)
    {
        if ((path.Length == 0 && !rootAllowed) || path.Contains('\\') || path.StartsWith('/') || path.Contains('\0') || path.Split('/').Any(p => p is "." or "..") || path.Contains("//")) throw new InvalidDataException("资源路径不正确");
    }
    private static string Text(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
    private static bool IsWad(string name) => name.EndsWith(".wad.client", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".riftmod.client", StringComparison.OrdinalIgnoreCase);
    private static async Task CopyLimitedAsync(Stream input, Stream output, long maximum, Action<long>? progress, CancellationToken token)
    {
        var buffer = new byte[81920]; long total = 0; int count;
        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            total += count; if (total > maximum) throw new InvalidDataException("资源接口响应超过大小限制");
            await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false); progress?.Invoke(total);
        }
    }
    public void Dispose() => _http.Dispose();
}
