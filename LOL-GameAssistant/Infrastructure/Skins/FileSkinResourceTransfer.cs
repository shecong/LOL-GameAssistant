using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using LOL_GameAssistant.Application.Skins;

namespace LOL_GameAssistant.Infrastructure.Skins;

/// <summary>小清单有界读取；资源按内容哈希续传，换源后仍校验完整包。</summary>
internal sealed class FileSkinResourceTransfer(HttpClient http, bool allowHttp, Action<string> status,
    TimeSpan? idleTimeout = null)
{
    internal const long MaxBytes = 2L * 1024 * 1024 * 1024;
    internal static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly TimeSpan _idle = idleTimeout ?? TimeSpan.FromSeconds(40);

    internal static bool ValidUri(Uri uri, bool allowHttp) => uri.IsAbsoluteUri && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Fragment) && (uri.Scheme == "https" || allowHttp && uri.Scheme == "http");
    internal static FileSkinResourceRelease Validate(FileSkinResourceRelease release, bool allowHttp)
    {
        if (release is null || string.IsNullOrWhiteSpace(release.Version) || release.Version.Length > 64 || release.Version != "legacy" && !Version.TryParse(release.Version, out _) || release.GamePatch is null || release.GamePatch.Length > 32
            || release.Notes is null || release.Notes.Length > 4000 || release.Sha256 is null || release.Sha256.Length != 64 || !release.Sha256.All(Uri.IsHexDigit)
            || release.Urls is not { Length: > 0 and <= 8 } || release.Size < 0 || release.Size > MaxBytes
            || release.Urls.Any(url => !Uri.TryCreate(url, UriKind.Absolute, out var uri) || !ValidUri(uri, allowHttp)))
            throw new InvalidDataException("换肤资源版本清单无效。");
        return release;
    }
    private async Task<HttpResponseMessage> SendAsync(Uri uri, long offset, CancellationToken token)
    {
        for (int redirect = 0; ; redirect++)
        {
            if (!ValidUri(uri, allowHttp)) throw new InvalidDataException("资源下载地址无效。");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(_idle);
            HttpResponseMessage response;
            try { response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new IOException("资源请求超时。"); }
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                var next = new Uri(uri, location);
                response.Dispose();
                if (redirect >= 5 || uri.Scheme == "https" && next.Scheme != "https") throw new IOException("资源下载重定向无效。");
                uri = next; continue;
            }
            return response;
        }
    }
    private async Task<int> ReadAsync(Stream input, Memory<byte> buffer, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(_idle);
        try { return await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new IOException("资源下载长时间无数据，正在尝试重连或备用源。"); }
    }
    internal async Task<FileSkinResourceRelease> ReadManifestAsync(string url, CancellationToken token)
    {
        using var total = CancellationTokenSource.CreateLinkedTokenSource(token);
        total.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await SendAsync(new Uri(url), 0, total.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 65536) throw new InvalidDataException("资源版本清单过大。");
        await using var input = await response.Content.ReadAsStreamAsync(total.Token).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[8192]; int count;
        while ((count = await ReadAsync(input, buffer, total.Token).ConfigureAwait(false)) > 0)
        {
            if (output.Length + count > 65536) throw new InvalidDataException("资源版本清单过大。");
            output.Write(buffer, 0, count);
        }
        return Validate(JsonSerializer.Deserialize<FileSkinResourceRelease>(output.ToArray(), Json)
            ?? throw new InvalidDataException("资源版本清单为空。"), allowHttp);
    }
    private static async Task<bool> VerifyAsync(string file, FileSkinResourceRelease release, CancellationToken token)
    {
        if (!File.Exists(file) || new FileInfo(file).Length > MaxBytes || release.Size > 0 && new FileInfo(file).Length != release.Size) return false;
        await using var stream = File.OpenRead(file);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase);
    }
    internal async Task DownloadAsync(FileSkinResourceRelease release, string file, bool preferGitHub, CancellationToken token)
    {
        Validate(release, allowHttp);
        if (await VerifyAsync(file, release, token).ConfigureAwait(false)) return;
        if (File.Exists(file) && (new FileInfo(file).Length > MaxBytes || release.Size > 0 && new FileInfo(file).Length >= release.Size)) File.Delete(file);
        Exception? last = null;
        foreach (var url in release.Urls.OrderBy(url => new Uri(url).Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) == preferGitHub ? 0 : 1))
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using var total = CancellationTokenSource.CreateLinkedTokenSource(token);
                    total.CancelAfter(TimeSpan.FromMinutes(30));
                    long offset = File.Exists(file) ? new FileInfo(file).Length : 0;
                    status(offset > 0 ? $"续传换肤资源（{new Uri(url).Host}）：已下载 {offset / 1048576d:F1} MB" : $"正在下载换肤资源（{new Uri(url).Host}）…");
                    using var response = await SendAsync(new Uri(url), offset, total.Token).ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                    {
                        if (await VerifyAsync(file, release, token).ConfigureAwait(false)) return;
                        File.Delete(file); throw new IOException("续传范围已失效，将重新下载。");
                    }
                    if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
                    { last = new IOException($"{new Uri(url).Host} 返回 {(int)response.StatusCode}"); break; }
                    response.EnsureSuccessStatusCode();
                    bool append = response.StatusCode == HttpStatusCode.PartialContent;
                    var range = response.Content.Headers.ContentRange;
                    if (append && (range?.Unit != "bytes" || range.From != offset || !range.To.HasValue || !range.Length.HasValue
                        || range.Length > MaxBytes || range.To < range.From || range.To >= range.Length
                        || release.Size > 0 && range.Length != release.Size))
                        throw new InvalidDataException("服务器返回的续传范围无效。");
                    long start = append ? offset : 0;
                    long? expectedEnd = append ? range!.To + 1 : response.Content.Headers.ContentLength;
                    if (expectedEnd > MaxBytes) throw new InvalidDataException("资源下载超过大小限制。");
                    await using (var output = new FileStream(file, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
                    await using (var input = await response.Content.ReadAsStreamAsync(total.Token).ConfigureAwait(false))
                    {
                        byte[] buffer = new byte[81920]; int count; long received = start;
                        var progress = System.Diagnostics.Stopwatch.StartNew();
                        while ((count = await ReadAsync(input, buffer, total.Token).ConfigureAwait(false)) > 0)
                        {
                            received += count;
                            if (received > MaxBytes || expectedEnd.HasValue && received > expectedEnd || release.Size > 0 && received > release.Size)
                                throw new InvalidDataException("资源长度超过声明大小。");
                            await output.WriteAsync(buffer.AsMemory(0, count), total.Token).ConfigureAwait(false);
                            if (progress.ElapsedMilliseconds >= 500)
                            { status($"正在下载换肤资源：{received / 1048576d:F1} MB"); progress.Restart(); }
                        }
                        if (expectedEnd.HasValue && received != expectedEnd) throw new IOException("资源传输中断，已保留断点。");
                    }
                    if (!await VerifyAsync(file, release, token).ConfigureAwait(false))
                    {
                        // 分段响应可能只包含完整文件的一部分；合法部分继续请求。
                        if (append && range!.To + 1 < range.Length) throw new IOException("继续下载剩余资源。");
                        throw new InvalidDataException("下载的资源包校验失败，将重新下载。");
                    }
                    return;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
                {
                    last = error;
                    if (error is InvalidDataException && File.Exists(file)) File.Delete(file);
                    status("下载未完成，尝试重连或备用源；断点已保留。");
                }
            }
        }
        throw new IOException("换肤资源下载失败，可再次点击下载继续：" + last?.Message, last);
    }
}
