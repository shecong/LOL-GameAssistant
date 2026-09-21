using Newtonsoft.Json;
using System.Text;

namespace LOL_GameAssistant.Helper
{
    /// <summary>
    /// Stream 扩展：JSON / Base64 JSON / 纯文本异步读取。
    /// </summary>
    public static class StreamExtensions
    {
        /// <summary>
        /// 读取并反序列化 JSON，整块在线程池上执行。
        /// 原先标了 async 却一次 await 都没有：调用方 await 它时任务已同步完成，
        /// 于是"阻塞读取响应体 + 解析"全压在调用者线程（通常是 UI 线程）上。
        /// </summary>
        public static Task<T?> ReadAsJsonAsync<T>(this Stream stream) => Task.Run(() =>
        {
            using (StreamReader reader = new StreamReader(stream))
            using (JsonTextReader jsonReader = new JsonTextReader(reader))
            {
                JsonSerializer serializer = new JsonSerializer();
                return serializer.Deserialize<T>(jsonReader);
            }
        });

        public static async Task<T?> ReadAsBase64JsonAsync<T>(this Stream stream)
        {
            using (StreamReader reader = new StreamReader(stream))
            {
                string base64String = await reader.ReadToEndAsync().ConfigureAwait(false);
                byte[] dataBytes = Convert.FromBase64String(base64String);
                string jsonString = Encoding.UTF8.GetString(dataBytes);
                return JsonConvert.DeserializeObject<T>(jsonString);
            }
        }

        public static async Task<string> ReadAsStringJsonAsync(this Stream stream)
        {
            using (StreamReader reader = new StreamReader(stream))
            {
                string content = await reader.ReadToEndAsync().ConfigureAwait(false);
                return content;
            }
        }
    }
}