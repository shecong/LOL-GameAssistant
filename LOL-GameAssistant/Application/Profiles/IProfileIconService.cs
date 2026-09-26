namespace LOL_GameAssistant.Application.Profiles;

/// <summary>读取玩家头像原始字节；图片解码属于表现层职责。</summary>
public interface IProfileIconService
{
    /// <summary>读取客户端资源目录中的头像 ID，用于分页式可视化选择。</summary>
    Task<IReadOnlyList<ProfileIconChoice>> GetProfileIconsAsync(CancellationToken cancellationToken = default);

    Task<byte[]?> GetProfileIconAsync(int iconId, CancellationToken cancellationToken = default);
}

/// <summary>召唤师头像目录条目；图像字节通过 <see cref="IProfileIconService.GetProfileIconAsync"/> 按需读取。</summary>
public sealed record ProfileIconChoice(int IconId);