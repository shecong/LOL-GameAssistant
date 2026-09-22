using LOL_GameAssistant.Domain.ApplicationInfo;

namespace LOL_GameAssistant.Application.ApplicationInfo;

/// <summary>查询应用可用更新的端口。</summary>
public interface IUpdateReleaseService
{
    Task<UpdateRelease?> GetLatestAsync(CancellationToken cancellationToken = default);
}