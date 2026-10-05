namespace LOL_GameApi.Middleware
{
    /// <summary>
    /// 全局异常处理中间件：捕获未处理异常，返回统一错误结构并记录日志。
    /// </summary>
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        /// <summary>初始化 ExceptionHandlingMiddleware 的实例状态，并保存传入的依赖或数据。</summary>
        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        /// <summary>执行后续 HTTP 请求管线，并将未处理异常转换为统一错误响应。</summary>
        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "未处理异常: {Path}", context.Request.Path);
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    message = "服务器内部错误",
                    errorCode = "INTERNAL_ERROR"
                });
            }
        }
    }
}