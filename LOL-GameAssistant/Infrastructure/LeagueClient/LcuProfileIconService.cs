using LOL_GameAssistant.Application.LeagueClient;
using LOL_GameAssistant.Application.Profiles;

namespace LOL_GameAssistant.Infrastructure.LeagueClient;

/// <summary>通过本机 LCU 读取头像字节，避免表现层直接调用资源 API。</summary>
public sealed class LcuProfileIconService : IProfileIconService
{
    private readonly ILcuRequestSender _requestSender;

    public LcuProfileIconService(ILcuRequestSender requestSender)
    {
        _requestSender = requestSender;
    }

    public Task<byte[]?> GetProfileIconAsync(int iconId, CancellationToken cancellationToken = default)
    {
        if (iconId <= 0) return Task.FromResult<byte[]?>(null);
        return _requestSender.GetBytesAsync($"/lol-game-data/assets/v1/profile-icons/{iconId}.jpg", cancellationToken);
    }
}
