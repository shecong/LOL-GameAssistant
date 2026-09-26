using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Profiles;
using Newtonsoft.Json.Linq;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>通过本机 LCU 读取头像字节，避免表现层直接调用资源 API。</summary>
public sealed class LcuProfileIconService : IProfileIconService
{
    private readonly ILcuRequestSender _requestSender;

    public LcuProfileIconService(ILcuRequestSender requestSender)
    {
        _requestSender = requestSender;
    }

    public async Task<IReadOnlyList<ProfileIconChoice>> GetProfileIconsAsync(CancellationToken cancellationToken = default)
    {
        string? json = await _requestSender.GetStringAsync("/lol-game-data/assets/v1/profile-icons.json", cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<ProfileIconChoice>();
        try
        {
            JToken root = JToken.Parse(json);
            IEnumerable<JObject> icons = root switch
            {
                JArray array => array.OfType<JObject>(),
                JObject map => map.Properties().Select(property => property.Value).OfType<JObject>(),
                _ => Enumerable.Empty<JObject>()
            };
            return icons
                .Select(item => item.Value<int?>("id") ?? 0)
                .Where(id => id > 0)
                .Distinct()
                .OrderBy(id => id)
                .Select(id => new ProfileIconChoice(id))
                .ToArray();
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return Array.Empty<ProfileIconChoice>();
        }
    }

    public Task<byte[]?> GetProfileIconAsync(int iconId, CancellationToken cancellationToken = default)
    {
        if (iconId <= 0) return Task.FromResult<byte[]?>(null);
        return _requestSender.GetBytesAsync($"/lol-game-data/assets/v1/profile-icons/{iconId}.jpg", cancellationToken);
    }
}