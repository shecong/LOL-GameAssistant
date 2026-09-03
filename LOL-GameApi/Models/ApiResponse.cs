namespace LOL_GameApi.Models
{
    /// <summary>
    /// 统一 API 响应包装：成功/失败状态 + 数据 + 消息。
    /// </summary>
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public T? Data { get; set; }
        public string? ErrorCode { get; set; }

        public static ApiResponse<T> Ok(T? data, string message = "ok")
            => new() { Success = true, Message = message, Data = data };

        public static ApiResponse<T> Fail(string message, string? errorCode = null)
            => new() { Success = false, Message = message, ErrorCode = errorCode };
    }
}
