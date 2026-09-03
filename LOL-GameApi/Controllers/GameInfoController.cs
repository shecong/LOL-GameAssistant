using LOL_GameApi.Models;
using LOL_GameApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace LOL_GameApi.Controllers
{
    /// <summary>
    /// 游戏基础信息接口（版本等）。
    /// </summary>
    [ApiController]
    [Route("api/game")]
    public class GameInfoController : ControllerBase
    {
        private readonly DataDragonService _dataDragon;

        public GameInfoController(DataDragonService dataDragon)
        {
            _dataDragon = dataDragon;
        }

        /// <summary>
        /// 获取最新游戏版本（DataDragon，带 6 小时缓存）。
        /// </summary>
        [HttpGet("version")]
        public async Task<ActionResult<ApiResponse<string?>>> GetVersion(CancellationToken cancellationToken)
        {
            string? version = await _dataDragon.GetLatestVersionAsync(cancellationToken);
            return version == null
                ? ApiResponse<string?>.Fail("版本获取失败", "VERSION_UNAVAILABLE")
                : ApiResponse<string?>.Ok(version);
        }
    }
}
