using System.Security.AccessControl;

namespace LOL_GameAssistant.Helper
{
    public class FileHelper
    {
        // 资源条目结构
        public class ResourceEntry
        {
            public string? FileName { get; set; }
            public string? FileType { get; set; }
            public byte[]? BinaryData { get; set; }
            public ResourceType ResourceType { get; set; }
        }

        public static class AssetManager
        {
            private static readonly string BasePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
            private static readonly Dictionary<string, ResourceEntry> ResourceCache = new Dictionary<string, ResourceEntry>();

            static AssetManager()
            {
                // 确保资源目录存在
                Directory.CreateDirectory(BasePath);
            }

            // 检查资源是否存在
            public static bool IsExist(string key)
            {
                // 检查内存缓存
                if (ResourceCache.ContainsKey(key))
                    return true;

                // 检查文件系统
                string filePath = GetFilePath(key);
                return File.Exists(filePath);
            }

            // 存储资源条目
            public static void StoreEntry(string key, ResourceEntry? entry)
            {
                // 添加到内存缓存
                ResourceCache[key] = entry;
                // 保存到文件系统
                string filePath = GetFilePath(key);
                File.WriteAllBytes(filePath, entry.BinaryData);
            }

            // 获取资源文件路径
            private static string GetFilePath(string key)
            {
                string extension = GetFileExtension(key);
                return Path.Combine(BasePath, $"{key}{extension}");
            }

            // 根据资源类型获取文件扩展名
            private static string GetFileExtension(string key)
            {
                //if (key.StartsWith(ResourceType.Profile.ToString()))
                //    return ".jpg";

                return ".bin"; // 默认扩展名
            }
        }
    }
}