using LOL_GameAssistant.Application.Files;
using System.Text;

namespace LOL_GameAssistant.Infrastructure.Files;

/// <summary>受用户选择路径约束的 UTF-8 文本导出实现。</summary>
public sealed class TextExportService : ITextExportService
{
    /// <summary>使用带 BOM 的 UTF-8 编码保存文本，便于表格软件识别中文。</summary>
    public async Task SaveUtf8WithBomAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string target = Path.GetFullPath(path);
        string temporary = Path.Combine(Path.GetDirectoryName(target)!, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
