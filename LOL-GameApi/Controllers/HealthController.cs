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
