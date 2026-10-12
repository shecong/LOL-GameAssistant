namespace LOL_GameAssistant.Infrastructure.Files;

/// <summary>只遍历指定缓存目录内的普通文件；不跨越符号链接、目录联接或重解析点。</summary>
internal static class CacheFiles
{
    internal static (long Bytes, int Files, int Errors) Measure(string root, CancellationToken token)
    {
        long bytes = 0; int count = 0; int errors = 0;
        Walk(root, token, file => { bytes += new FileInfo(file).Length; count++; }, () => errors++);
        return (bytes, count, errors);
    }
    internal static (int Deleted, int Errors) Clear(string root, CancellationToken token)
    {
        int count = 0; int errors = 0;
        Walk(root, token, file => { File.Delete(file); count++; }, () => errors++);
        return (count, errors);
    }
    internal static void ValidateRoot(string root)
    {
        for (DirectoryInfo? directory = new(Path.GetFullPath(root)); directory != null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("缓存目录包含目录联接或符号链接，已跳过清理。");
    }
    private static void Walk(string root, CancellationToken token, Action<string> fileAction, Action error)
    {
        ValidateRoot(root);
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Directory.Exists(full)) return;
        var pending = new Stack<string>(); pending.Push(full);
        while (pending.TryPop(out string? current))
        {
            token.ThrowIfCancellationRequested();
            string[] entries;
            try { ValidateRoot(current); entries = Directory.GetFileSystemEntries(current); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { error(); continue; }
            foreach (string entry in entries)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    string path = Path.GetFullPath(entry);
                    if (!path.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("缓存文件路径越界。");
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) { error(); continue; }
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                    else fileAction(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { error(); }
            }
        }
    }
}
