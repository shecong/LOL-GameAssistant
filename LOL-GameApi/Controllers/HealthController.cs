using LOL_GameApi.Models;
using Microsoft.AspNetCore.Mvc;

namespace LOL_GameApi.Controllers
{
    /// <summary>
    /// 服务健康检查。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController : ControllerBase
    {
        /// <summary>就绪检查共享有限时长的刷新；旧缓存仍可用，但明确标记降级。</summary>
        [HttpGet("ready")]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("version")]
        public async Task<IActionResult> Ready([FromServices] Services.DataDragonService dataDragon, CancellationToken cancellationToken)
        {
            await dataDragon.GetLatestVersionAsync(cancellationToken);
            var status = dataDragon.GetStatus();
            return StatusCode(status.HasCache ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable,
                new { ready = status.HasCache, degraded = status.IsStale, dataDragon = status });
        }

        /// <summary>
        /// 返回服务状态、版本号与服务器时间。
        /// </summary>
        [HttpGet]
        public ActionResult<ApiResponse<object>> Get()
        {
            string version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0";
            return ApiResponse<object>.Ok(new
            {
                service = "LOL-GameApi",
                version,
                serverTime = DateTimeOffset.Now
            });
        }
    }
}
