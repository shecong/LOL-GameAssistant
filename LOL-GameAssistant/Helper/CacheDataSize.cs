using System.Text;
using Newtonsoft.Json;

namespace LOL_GameAssistant.Helper;

internal static class CacheDataSize
{
    internal static (int Entries, long Bytes) Measure(IEnumerable<object?> values)
    {
        object[] snapshot = values.Where(value => value != null).Cast<object>().ToArray();
        return (snapshot.Length, snapshot.Sum(value => (long)Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(value))));
    }
}
