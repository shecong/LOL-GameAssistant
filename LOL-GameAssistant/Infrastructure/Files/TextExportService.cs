using LOL_GameAssistant.Application.Files;
using System.Text;

namespace LOL_GameAssistant.Infrastructure.Files;

/// <summary>受用户选择路径约束的 UTF-8 文本导出实现。</summary>
public sealed class TextExportService : ITextExportService
{
    /// <summary>使用带 BOM 的 UTF-8 编码保存文本，便于表格软件识别中文。</summary>
    public Task SaveUtf8WithBomAsync(string path, string content, CancellationToken cancellationToken = default) =>
        File.WriteAllTextAsync(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken);
}