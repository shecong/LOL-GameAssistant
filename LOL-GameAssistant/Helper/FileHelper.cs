using System.Security.AccessControl;

namespace LOL_GameAssistant.Helper
{
    /// <summary>
    /// 资源条目结构
    /// </summary>
    public class ResourceEntry
    {
        public string? FileName { get; set; }
        public string? FileType { get; set; }
        public byte[]? BinaryData { get; set; }
        public string? ResourceType { get; set; } // 改为字符串避免依赖不存在的枚举
    }

    public static class AssetManager
    {
        private static readonly string BasePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
        private static readonly Dictionary<string, ResourceEntry> ResourceCache = new Dictionary<string, ResourceEntry>();
        private static readonly object _lock = new object();

        static AssetManager()
        {
            Directory.CreateDirectory(BasePath);
        }

        /// <summary>
        /// 检查资源是否存在
        /// </summary>
        public static bool IsExist(string key)
        {
            lock (_lock)
            {
                if (ResourceCache.ContainsKey(key))
                    return true;
            }
            string filePath = GetFilePath(key);
            return File.Exists(filePath);
        }

        /// <summary>
        /// 存储资源条目
        /// </summary>
        public static void StoreEntry(string key, ResourceEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (entry.BinaryData == null) throw new ArgumentNullException(nameof(entry.BinaryData));

            lock (_lock)
            {
                ResourceCache[key] = entry;
            }
            string filePath = GetFilePath(key);
            File.WriteAllBytes(filePath, entry.BinaryData);
        }

        private static string GetFilePath(string key)
        {
            return Path.Combine(BasePath, $"{key}.bin");
        }
    }
}
