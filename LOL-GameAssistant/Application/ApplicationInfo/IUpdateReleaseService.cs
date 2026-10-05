using LOL_GameAssistant.Domain.ApplicationInfo;

namespace LOL_GameAssistant.Application.ApplicationInfo;

/// <summary>查询应用可用更新的端口。</summary>
public interface IUpdateReleaseService
{
    /// <summary>查询最新发布版本信息。</summary>
    Task<UpdateRelease?> GetLatestAsync(CancellationToken cancellationToken = default);
}