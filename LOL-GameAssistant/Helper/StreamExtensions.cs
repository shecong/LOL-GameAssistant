using Newtonsoft.Json;

namespace LOL_GameAssistant.Helper
{
    /// <summary>
    /// Stream 扩展：JSON / 纯文本异步读取。
    /// </summary>
    public static class StreamExtensions
    {
        /// <summary>
        /// 读取并反序列化 JSON，整块在线程池上执行。
        /// 原先标了 async 却一次 await 都没有：调用方 await 它时任务已同步完成，
        /// 于是"阻塞读取响应体 + 解析"全压在调用者线程（通常是 UI 线程）上。
        /// </summary>
        public static Task<T?> ReadAsJsonAsync<T>(this Stream stream, CancellationToken cancellationToken = default) => Task.Run(() =>
        {
            using (StreamReader reader = new StreamReader(stream))
            using (JsonTextReader jsonReader = new JsonTextReader(reader))
            {
                cancellationToken.ThrowIfCancellationRequested();
                JsonSerializer serializer = new JsonSerializer();
                T? result = serializer.Deserialize<T>(jsonReader);
                cancellationToken.ThrowIfCancellationRequested();
                return result;
            }
        });

        /// <summary>异步读取响应流中的原始文本，并在读取结束后关闭流。</summary>
        public static async Task<string> ReadAsStringJsonAsync(this Stream stream, CancellationToken cancellationToken = default)
        {
            using (StreamReader reader = new StreamReader(stream))
            {
                string content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                return content;
            }
        }
    }
}
