namespace LOL_GameAssistant.Application.Files;

/// <summary>将用户明确选择的导出内容写入文本文件的端口。</summary>
public interface ITextExportService
{
    Task SaveUtf8WithBomAsync(string path, string content, CancellationToken cancellationToken = default);
}
