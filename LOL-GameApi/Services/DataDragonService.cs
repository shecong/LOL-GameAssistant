using System.Text.Json;
using Microsoft.Extensions.Options;

namespace LOL_GameApi.Services;

public sealed class DataDragonOptions
{
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromHours(6);
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan FailureCooldown { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan MaxFailureCooldown { get; set; } = TimeSpan.FromMinutes(5);
    public int MaxResponseBytes { get; set; } = 64 * 1024;
}

public sealed record DataDragonStatus(bool HasCache, bool IsStale, DateTimeOffset? LastSuccess,
    DateTimeOffset? LastFailure, DateTimeOffset? NextRetry, int ConsecutiveFailures);

/// <summary>共享刷新任务、有限超时与指数冷却；调用方取消只结束自己的等待。</summary>
public sealed class DataDragonService
{
    private readonly object _sync = new();
    private readonly HttpClient _http;
    private readonly ILogger<DataDragonService> _logger;
    private readonly TimeProvider _clock;
    private readonly DataDragonOptions _options;
    private Task<string?>? _refresh;
    private string? _latestVersion;
    private DateTimeOffset? _lastSuccess;
    private DateTimeOffset? _lastFailure;
    private DateTimeOffset? _nextRetry;
    private int _failures;

    public DataDragonService(HttpClient http, ILogger<DataDragonService> logger,
        TimeProvider clock, IOptions<DataDragonOptions> options)
    {
        _http = http;
        _logger = logger;
        _clock = clock;
        _options = options.Value;
    }

    public Task<string?> GetLatestVersionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task<string?> refresh;
        lock (_sync)
        {
            var now = _clock.GetUtcNow();
            if (IsFresh(now) || (_nextRetry.HasValue && now < _nextRetry.Value))
                return Task.FromResult(_latestVersion);
            refresh = _refresh ??= Task.Run(RefreshAsync);
        }
        return refresh.WaitAsync(cancellationToken);
    }

    public DataDragonStatus GetStatus()
    {
        lock (_sync)
            return new(_latestVersion != null, !IsFresh(_clock.GetUtcNow()),
                _lastSuccess, _lastFailure, _nextRetry, _failures);
    }

    private bool IsFresh(DateTimeOffset now) => _latestVersion != null && _lastSuccess.HasValue &&
        now >= _lastSuccess.Value && now - _lastSuccess.Value < _options.CacheDuration;

    private async Task<string?> RefreshAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(_options.RequestTimeout, _clock);
            using var response = await _http.GetAsync(
                "https://ddragon.leagueoflegends.com/api/versions.json",
                HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > _options.MaxResponseBytes)
                throw new InvalidDataException("DataDragon response exceeds the size limit.");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var body = new MemoryStream();
            var buffer = new byte[4096];
            int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (body.Length + count > _options.MaxResponseBytes)
                    throw new InvalidDataException("DataDragon response exceeds the size limit.");
                body.Write(buffer, 0, count);
            }
            var versions = JsonSerializer.Deserialize<List<string>>(body.GetBuffer().AsSpan(0, (int)body.Length));
            if (versions is not { Count: > 0 } || !Version.TryParse(versions[0], out _))
                throw new InvalidDataException("DataDragon returned no valid version.");
            lock (_sync)
            {
                _latestVersion = versions[0];
                _lastSuccess = _clock.GetUtcNow();
                _nextRetry = null;
                _failures = 0;
            }
            _logger.LogInformation("DataDragon version refresh succeeded");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or IOException or InvalidDataException)
        {
            lock (_sync)
            {
                _lastFailure = _clock.GetUtcNow();
                _failures = Math.Min(_failures + 1, 20);
                var ticks = Math.Min(_options.MaxFailureCooldown.Ticks,
                    _options.FailureCooldown.Ticks * Math.Pow(2, _failures - 1));
                _nextRetry = _lastFailure.Value + TimeSpan.FromTicks((long)ticks);
            }
            _logger.LogWarning(ex, "DataDragon refresh failed; returning cached version when available");
        }
        finally
        {
            lock (_sync) _refresh = null;
        }
        lock (_sync) return _latestVersion;
    }
}
