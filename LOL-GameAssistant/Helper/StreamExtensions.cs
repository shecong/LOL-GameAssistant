using Newtonsoft.Json;
using System.Text;

namespace LOL_GameAssistant.Helper
{
    public static class StreamExtensions
    {
        public static async Task<T> ReadAsJsonAsync<T>(this Stream stream)
        {
            using (StreamReader reader = new StreamReader(stream))
            using (JsonTextReader jsonReader = new JsonTextReader(reader))
            {
                JsonSerializer serializer = new JsonSerializer();
                return serializer.Deserialize<T>(jsonReader);
            }
        }

        public static async Task<T> ReadAsBase64JsonAsync<T>(this Stream stream)
        {
            using (StreamReader reader = new StreamReader(stream))
            {
                string base64String = await reader.ReadToEndAsync();
                byte[] dataBytes = Convert.FromBase64String(base64String);
                string jsonString = Encoding.UTF8.GetString(dataBytes);
                return JsonConvert.DeserializeObject<T>(jsonString);
            }
        }

        public static async Task<string> ReadAsStringJsonAsync<String>(this Stream stream)
        {
            using (StreamReader reader = new StreamReader(stream))
            {
                // 4. ReadToEndAsync() 会将流中的所有内容异步读取到一个字符串中
                string content = await reader.ReadToEndAsync();
                return content;
            }
        }
    }
}