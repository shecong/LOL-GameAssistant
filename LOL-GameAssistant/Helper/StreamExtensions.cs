using Newtonsoft.Json;
using System.Text;

namespace LOL_GameAssistant.Helper
{
    public static class StreamExtensions
    {
        /// <summary>
        /// 将流反序列化为指定类型（同步实现，移除无意义的 async）
        /// </summary>
        public static T ReadAsJsonAsync<T>(this Stream stream)
        {
            using (StreamReader reader = new StreamReader(stream))
            using (JsonTextReader jsonReader = new JsonTextReader(reader))
            {
                JsonSerializer serializer = new JsonSerializer();
                return serializer.Deserialize<T>(jsonReader)!;
            }
        }

        /// <summary>
        /// 将 Base64 编码的 JSON 流反序列化为指定类型
        /// </summary>
        public static async Task<T> ReadAsBase64JsonAsync<T>(this Stream stream)
        {
            using (StreamReader reader = new StreamReader(stream))
            {
                string base64String = await reader.ReadToEndAsync();
                if (string.IsNullOrEmpty(base64String))
                    return default!;

                byte[] dataBytes = Convert.FromBase64String(base64String);
                string jsonString = Encoding.UTF8.GetString(dataBytes);
                return JsonConvert.DeserializeObject<T>(jsonString)!;
            }
        }

        /// <summary>
        /// 将流内容读取为字符串（移除虚假泛型参数）
        /// </summary>
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
