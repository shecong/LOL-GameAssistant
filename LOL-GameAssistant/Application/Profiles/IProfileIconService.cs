namespace LOL_GameAssistant.Application.Profiles;

/// <summary>读取玩家头像原始字节；图片解码属于表现层职责。</summary>
public interface IProfileIconService
{
    Task<byte[]?> GetProfileIconAsync(int iconId, CancellationToken cancellationToken = default);
}