using Newtonsoft.Json;
using System.Text;

namespace LOL_GameAssistant.Helper
{
    /// <summary>
    /// Stream 扩展：JSON / Base64 JSON / 纯文本异步读取。
    /// </summary>
    public static class StreamExtensions
    {
        public static async Task<T?> ReadAsJsonAsync<T>(this Stream stream)
        {
            using (StreamReader reader = new StreamReader(stream))
            using (JsonTextReader jsonReader = new JsonTextReader(reader))
            {
                JsonSerializer serializer = new JsonSerializer();
                return serializer.Deserialize<T>(jsonReader);
            }
        }

        public static async Task<T?> ReadAsBase64JsonAsync<T>(this Stream stream)
        {
            using (StreamReader reader = new StreamReader(stream))
            {
                string base64String = await reader.ReadToEndAsync();
                byte[] dataBytes = Convert.FromBase64String(base64String);
                string jsonString = Encoding.UTF8.GetString(dataBytes);
                return JsonConvert.DeserializeObject<T>(jsonString);
            }
        }

        public static async Task<string> ReadAsStringJsonAsync(this Stream stream)
        {
            using (StreamReader reader = new StreamReader(stream))
            {
                string content = await reader.ReadToEndAsync();
                return content;
            }
        }
    }
}
